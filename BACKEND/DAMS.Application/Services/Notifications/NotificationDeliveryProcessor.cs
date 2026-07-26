using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// The worker that actually talks to providers.
    ///
    /// Correctness under concurrency rests on two things. A delivery is claimed by writing a
    /// lease to it under optimistic concurrency, so of two instances racing for the same row
    /// exactly one wins and the other moves on. And the lease expires, so a worker that is
    /// killed mid-send releases its rows automatically — a restart resumes rather than
    /// stalling, and never re-sends a channel that already succeeded, because success is
    /// recorded per channel.
    /// </summary>
    public sealed class NotificationDeliveryProcessor : INotificationDeliveryProcessor
    {
        private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}";

        /// <summary>
        /// States a sweep may claim. <see cref="NotificationDeliveryStatus.Processing"/> is in
        /// the list deliberately: combined with the lease check below, it is what lets a row
        /// abandoned by a worker that was killed mid-send be picked up again once its lease
        /// expires, instead of being stranded in Processing for ever.
        /// </summary>
        private static readonly NotificationDeliveryStatus[] Claimable =
        {
            NotificationDeliveryStatus.Pending,
            NotificationDeliveryStatus.Scheduled,
            NotificationDeliveryStatus.Retrying,
            NotificationDeliveryStatus.Processing
        };

        private readonly AppDbContext _context;
        private readonly IEnumerable<INotificationChannelSender> _senders;
        private readonly INotificationDispatcher _dispatcher;
        private readonly INotificationRecipientResolver _recipients;
        private readonly NotificationOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<NotificationDeliveryProcessor> _logger;

        public NotificationDeliveryProcessor(
            AppDbContext context,
            IEnumerable<INotificationChannelSender> senders,
            INotificationDispatcher dispatcher,
            INotificationRecipientResolver recipients,
            NotificationOptions options,
            TimeProvider clock,
            ILogger<NotificationDeliveryProcessor> logger)
        {
            _context = context;
            _senders = senders;
            _dispatcher = dispatcher;
            _recipients = recipients;
            _options = options;
            _clock = clock;
            _logger = logger;
        }

        public async Task<int> ProcessDueDeliveriesAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var claimed = await ClaimDeliveriesAsync(batchSize, now, cancellationToken);
            if (claimed.Count == 0)
                return 0;

            var processed = 0;
            foreach (var delivery in claimed)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ProcessOneAsync(delivery, cancellationToken);
                processed++;
            }

            return processed;
        }

        private async Task<List<NotificationDelivery>> ClaimDeliveriesAsync(
            int batchSize, DateTime now, CancellationToken cancellationToken)
        {
            var size = Math.Clamp(batchSize, 1, 500);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var candidates = await _context.NotificationDeliveries
                    .Include(d => d.Notification)
                    .Where(d => Claimable.Contains(d.Status)
                                && d.AvailableAt <= now
                                && (d.LockedUntil == null || d.LockedUntil < now))
                    .OrderBy(d => d.AvailableAt)
                    .ThenBy(d => d.Id)
                    .Take(size)
                    .ToListAsync(cancellationToken);

                if (candidates.Count == 0)
                    return candidates;

                foreach (var delivery in candidates)
                {
                    delivery.Status = NotificationDeliveryStatus.Processing;
                    delivery.ProcessingStartedAt = now;
                    delivery.LockedUntil = now.AddMinutes(_options.LeaseMinutes);
                    delivery.LockedBy = WorkerId;
                }

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return candidates;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // Another instance claimed some of these first. Drop the contended rows
                    // and let the next pass pick up whatever is genuinely still free.
                    foreach (var entry in ex.Entries)
                        entry.State = EntityState.Detached;

                    _context.ChangeTracker.Clear();
                }
            }

            _logger.LogWarning("Could not claim a delivery batch after repeated contention; the next sweep will retry.");
            return new List<NotificationDelivery>();
        }

        private async Task ProcessOneAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            delivery.AttemptCount++;
            delivery.LastAttemptAt = now;

            var notification = delivery.Notification;

            // An expired notification is not worth a provider call — it would arrive telling
            // somebody about something that no longer matters.
            if (notification.ExpiresAt.HasValue && notification.ExpiresAt.Value <= now)
            {
                Finish(delivery, NotificationDeliveryStatus.Expired, "The notification expired before it could be sent.", permanent: true, now);
                await SaveAsync(cancellationToken);
                return;
            }

            var sender = _senders.FirstOrDefault(s => s.Channel == delivery.Channel);
            if (sender == null)
            {
                Finish(delivery, NotificationDeliveryStatus.Failed, $"No sender is registered for the {delivery.Channel} channel.", permanent: true, now);
                await SaveAsync(cancellationToken);
                return;
            }

            ChannelSendResult result;
            try
            {
                result = await sender.SendAsync(delivery, notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown: release the lease so the next run picks this up immediately
                // rather than waiting for the lease to expire.
                delivery.Status = NotificationDeliveryStatus.Retrying;
                delivery.LockedUntil = null;
                delivery.LockedBy = null;
                await SaveAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delivery {DeliveryId} on {Channel} threw.", delivery.Id, delivery.Channel);
                result = ChannelSendResult.TransientFailure(ex.Message);
            }

            delivery.Target = LeadContactNormalizer.LimitOrNull(result.Target, 300) ?? delivery.Target;
            delivery.ProviderReference = LeadContactNormalizer.LimitOrNull(result.ProviderReference, 200);

            switch (result.Status)
            {
                case NotificationDeliveryStatus.Sent:
                    delivery.Status = NotificationDeliveryStatus.Sent;
                    delivery.SentAt = now;
                    delivery.FailureReason = null;
                    delivery.IsPermanentFailure = false;
                    Release(delivery);
                    break;

                case NotificationDeliveryStatus.Skipped:
                case NotificationDeliveryStatus.Unavailable:
                case NotificationDeliveryStatus.Bounced:
                    Finish(delivery, result.Status, result.FailureReason, permanent: true, now);
                    break;

                default:
                    if (result.IsPermanent || delivery.AttemptCount >= _options.MaxAttempts)
                    {
                        Finish(delivery, NotificationDeliveryStatus.Failed,
                            result.IsPermanent
                                ? result.FailureReason
                                : $"{result.FailureReason} (gave up after {delivery.AttemptCount} attempts)",
                            permanent: true, now);
                    }
                    else
                    {
                        // Back off geometrically so a provider that is briefly down is not
                        // hammered, and a long outage does not consume every attempt at once.
                        var delay = TimeSpan.FromSeconds(
                            Math.Min(_options.BaseRetryDelaySeconds * Math.Pow(2, delivery.AttemptCount - 1),
                                     _options.MaxRetryDelayMinutes * 60));

                        delivery.Status = NotificationDeliveryStatus.Retrying;
                        delivery.AvailableAt = now.Add(delay);
                        delivery.FailureReason = LeadContactNormalizer.LimitOrNull(result.FailureReason, 1000);
                        Release(delivery);
                    }
                    break;
            }

            await SaveAsync(cancellationToken);
        }

        private static void Finish(NotificationDelivery delivery, NotificationDeliveryStatus status, string? reason, bool permanent, DateTime now)
        {
            delivery.Status = status;
            delivery.FailureReason = LeadContactNormalizer.LimitOrNull(reason, 1000);
            delivery.IsPermanentFailure = permanent;
            delivery.FailedAt = now;
            Release(delivery);
        }

        private static void Release(NotificationDelivery delivery)
        {
            delivery.LockedUntil = null;
            delivery.LockedBy = null;
        }

        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Somebody else finished this row (a manual retry, a lease takeover). Their
                // outcome stands; ours is discarded rather than overwriting it.
                _logger.LogInformation("A delivery row changed while it was being processed; the other outcome was kept.");
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
            }
            catch (DbUpdateException ex) when (
                NotificationDispatcher.IsDuplicateKey(ex)
                && ex.Entries.Any(e => e.Entity is EmailSuppression && e.State == EntityState.Added))
            {
                // Two workers can observe the same hard bounce at the same time. The unique
                // address index makes one suppression the winner; detach only the losing
                // insert and still persist this delivery's terminal result.
                foreach (var entry in ex.Entries.Where(e =>
                             e.Entity is EmailSuppression && e.State == EntityState.Added))
                    entry.State = EntityState.Detached;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        // ── Scheduled and manual sends ──────────────────────────────────────────────

        public async Task<int> ProcessScheduledJobsAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var size = Math.Clamp(batchSize, 1, 50);

            // Processing is claimable for the same reason deliveries are: a job whose worker
            // died half way through has to be resumable. Resuming is safe because every
            // recipient it already created is protected by that recipient's dedup key.
            var candidates = await _context.NotificationJobs
                .Where(j => (j.Status == NotificationJobStatus.Scheduled || j.Status == NotificationJobStatus.Processing)
                            && (j.ScheduledAt == null || j.ScheduledAt <= now)
                            && (j.LockedUntil == null || j.LockedUntil < now))
                .OrderBy(j => j.ScheduledAt ?? j.CreatedAt)
                .Take(size)
                .ToListAsync(cancellationToken);

            var processed = 0;

            foreach (var job in candidates)
            {
                // Claimed one at a time: a cancellation that lands between the read and the
                // claim makes the row's version stale, the claim fails, and the job stays
                // cancelled. A cancelled job can never be resurrected by a retry or a restart.
                job.Status = NotificationJobStatus.Processing;
                job.ProcessingStartedAt = now;
                job.LockedUntil = now.AddMinutes(_options.LeaseMinutes);
                job.LockedBy = WorkerId;

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                        entry.State = EntityState.Detached;

                    // The remaining candidate objects were read in the same stale snapshot.
                    // Stop this batch and let the next sweep re-query them; processing those
                    // detached objects could otherwise bypass a cancellation that just won.
                    return processed;
                }

                try
                {
                    var count = await FanOutAsync(job, cancellationToken);
                    job.RecipientCount = count;
                    job.Status = NotificationJobStatus.Sent;
                    job.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                    job.FailureReason = null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Put it back for the next run. Recipients already created are protected
                    // by their dedup keys, so resuming cannot double-send.
                    job.Status = NotificationJobStatus.Scheduled;
                    job.LockedUntil = null;
                    job.LockedBy = null;
                    await _context.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification job {JobId} failed.", job.Id);
                    job.Status = NotificationJobStatus.Failed;
                    job.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                    job.FailureReason = LeadContactNormalizer.Limit(ex.Message, 1000);
                }

                job.LockedUntil = null;
                job.LockedBy = null;
                await _context.SaveChangesAsync(cancellationToken);
                processed++;
            }

            return processed;
        }

        private async Task<int> FanOutAsync(NotificationJob job, CancellationToken cancellationToken)
        {
            var selection = DeserializeAudience(job.AudienceJson);
            var recipients = await _recipients.ResolveTargetsAsync(job.AudienceType, selection, cancellationToken);
            if (!job.Channels.HasFlag(NotificationChannel.Email))
                recipients = recipients.Where(r => r.UserId.HasValue).ToList();

            if (recipients.Count > _options.MaxBroadcastRecipients)
                throw new InvalidOperationException(
                    $"The audience resolved to {recipients.Count} recipients, above the {_options.MaxBroadcastRecipients} limit.");

            var created = 0;

            // Chunked so one save never holds thousands of rows, and so a crash part way
            // through keeps the recipients already written.
            foreach (var chunk in recipients.Chunk(200))
            {
                foreach (var recipient in chunk)
                {
                    var recipientKey = recipient.UserId.HasValue
                        ? $"u:{recipient.UserId.Value}"
                        : $"e:{ContactKey(recipient.Email!)}";
                    await _dispatcher.QueueAsync(new NotificationRequest
                    {
                        Type = job.Type,
                        RecipientUserId = recipient.UserId,
                        RecipientEmail = recipient.Email,
                        RecipientName = recipient.Name,
                        // The job id is what makes a resumed or retried fan-out idempotent.
                        DedupKey = $"job:{job.Id}:{recipientKey}",
                        Title = job.Title,
                        Message = job.Message,
                        Priority = job.Priority,
                        CreatedByUserId = job.CreatedByUserId,
                        DeepLink = job.ActionUrl,
                        EntityType = NotificationEntityType.Announcement,
                        EntityId = job.Id,
                        JobId = job.Id,
                        ChannelMask = job.Channels,
                        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["title"] = job.Title,
                            ["message"] = job.Message
                        }
                    }, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                created += chunk.Length;
            }

            return created;
        }

        private static string ContactKey(string email)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
            return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
        }

        private static NotificationAudienceSelection DeserializeAudience(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new NotificationAudienceSelection();

            try
            {
                return JsonSerializer.Deserialize<NotificationAudienceSelection>(json) ?? new NotificationAudienceSelection();
            }
            catch (JsonException)
            {
                return new NotificationAudienceSelection();
            }
        }

        // ── Housekeeping ────────────────────────────────────────────────────────────

        public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var removed = 0;

            // Inbox retention must never delete the event row: that row is also the durable
            // dedup tombstone and the parent of delivery/audit history.
            var expired = await _context.Notifications
                .Where(n => !n.IsArchived && n.ExpiresAt != null && n.ExpiresAt < now)
                .Take(500)
                .ToListAsync(cancellationToken);

            foreach (var notification in expired)
            {
                notification.IsArchived = true;
                notification.ArchivedAt ??= now;
                removed++;
            }

            if (_options.InboxRetentionDays > 0)
            {
                var cutoff = now.AddDays(-_options.InboxRetentionDays);
                var stale = await _context.Notifications
                    .Where(n => !n.IsArchived && n.IsRead && n.CreatedAt < cutoff)
                    .Take(500)
                    .ToListAsync(cancellationToken);

                foreach (var notification in stale)
                {
                    notification.IsArchived = true;
                    notification.ArchivedAt ??= now;
                    removed++;
                }
            }

            if (removed > 0)
                await _context.SaveChangesAsync(cancellationToken);

            return removed;
        }
    }
}
