using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Manual and scheduled admin sends, plus the delivery history an admin needs to see what
    /// actually happened.
    ///
    /// Every send goes through a job rather than writing notifications inline. That is what
    /// makes "send now" and "send at 9am tomorrow" the same code path, what makes a cancelled
    /// schedule stay cancelled across restarts, and what lets a double-clicked Send collapse
    /// into one job instead of two broadcasts.
    /// </summary>
    public sealed class NotificationAdminService : INotificationAdminService
    {
        private readonly AppDbContext _context;
        private readonly INotificationRecipientResolver _recipients;
        private readonly NotificationOptions _options;
        private readonly TimeProvider _clock;

        public NotificationAdminService(
            AppDbContext context,
            INotificationRecipientResolver recipients,
            NotificationOptions options,
            TimeProvider clock)
        {
            _context = context;
            _recipients = recipients;
            _options = options;
            _clock = clock;
        }

        // ── Audience preview ────────────────────────────────────────────────────────

        public async Task<AudiencePreviewDto> PreviewAudienceAsync(
            ComposeNotificationDto dto, CancellationToken cancellationToken = default)
        {
            var selection = ToSelection(dto.Audience);
            var targets = await _recipients.ResolveTargetsAsync(dto.Audience.Type, selection, cancellationToken);
            if (!dto.SendEmail)
                targets = targets.Where(t => t.UserId.HasValue).ToList();

            var recipients = targets.Where(t => t.UserId.HasValue).Select(t => t.UserId!.Value).ToList();
            var category = NotificationCatalog.CategoryOf(dto.Type);

            var withEmail = targets.Count(t => t.UserId == null && SmtpEmailSender.IsValidAddress(t.Email));
            var emailOptedOut = 0;
            var pushOptedOut = 0;
            var pushUsers = 0;
            var pushDevices = 0;
            var samples = new List<string>();

            if (recipients.Count > 0)
            {
                var ids = recipients.ToArray();

                var users = await _context.Users
                    .AsNoTracking()
                    .Where(u => ids.Contains(u.UserId))
                    .Select(u => new { u.UserId, u.Email, u.FullName })
                    .ToListAsync(cancellationToken);

                var customerAddresses = (await _context.Customers
                        .AsNoTracking()
                        .Where(c => c.UserId.HasValue && ids.Contains(c.UserId.Value))
                        .Select(c => new { UserId = c.UserId!.Value, c.Email, c.CreatedAt, c.UpdatedAt })
                        .ToListAsync(cancellationToken))
                    .GroupBy(c => c.UserId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                            .Select(c => c.Email)
                            .FirstOrDefault());

                withEmail += users.Count(u =>
                    customerAddresses.TryGetValue(u.UserId, out var customerEmail)
                        ? SmtpEmailSender.IsValidAddress(customerEmail) || SmtpEmailSender.IsValidAddress(u.Email)
                        : SmtpEmailSender.IsValidAddress(u.Email));
                samples = users.Take(5).Select(u => u.FullName).ToList();

                var devices = await _context.PushSubscriptions
                    .AsNoTracking()
                    .Where(s => ids.Contains(s.UserId) && s.IsActive)
                    .Select(s => new { s.UserId })
                    .ToListAsync(cancellationToken);

                pushDevices = devices.Count;
                pushUsers = devices.Select(d => d.UserId).Distinct().Count();

                if (!NotificationCatalog.IsMandatoryCategory(category))
                {
                    var preferences = await _context.NotificationPreferences
                        .AsNoTracking()
                        .Where(p => ids.Contains(p.UserId) && p.Category == category)
                        .ToListAsync(cancellationToken);

                    emailOptedOut = preferences.Count(p => !p.EmailEnabled);
                    pushOptedOut = preferences.Count(p => !p.PushEnabled);
                }
            }

            if (samples.Count < 5)
            {
                samples.AddRange(targets
                    .Where(t => !t.UserId.HasValue && !string.IsNullOrWhiteSpace(t.Name))
                    .Select(t => t.Name!)
                    .Take(5 - samples.Count));
            }

            return new AudiencePreviewDto
            {
                TotalRecipients = targets.Count,
                WithEmail = withEmail,
                WithPushDevices = pushUsers,
                PushDeviceCount = pushDevices,
                EmailOptedOut = emailOptedOut,
                PushOptedOut = pushOptedOut,
                RequiresConfirmation = targets.Count >= _options.LargeAudienceThreshold,
                Description = _recipients.Describe(dto.Audience.Type, selection),
                SampleRecipients = samples
            };
        }

        // ── Compose ─────────────────────────────────────────────────────────────────

        public async Task<NotificationJobDto> ComposeAsync(
            ComposeNotificationDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var title = LeadContactNormalizer.Clean(dto.Title)
                        ?? throw new InvalidOperationException("A title is required.");
            var message = LeadContactNormalizer.Clean(dto.Message)
                          ?? throw new InvalidOperationException("A message is required.");

            // The composer writes free text, so it is held to the same content rules as an
            // edited template — no script, no markup that could execute in a mailbox.
            NotificationTemplateRenderer.Validate(title, NotificationCatalog.CommonVariables, "Title");
            NotificationTemplateRenderer.Validate(message, NotificationCatalog.CommonVariables, "Message");

            var actionUrl = LeadContactNormalizer.Clean(dto.ActionUrl);
            if (actionUrl != null && NotificationLink.Sanitize(actionUrl) == null)
                throw new InvalidOperationException("The destination must be a path inside DAMS, for example /projects/3.");

            // Only broadcast-shaped types may be composed by hand. A crafted request must not
            // be able to fake a payment receipt or a security message.
            if (dto.Type is not (NotificationType.AdminAnnouncement or NotificationType.ProjectUpdated or NotificationType.AccountSecurity))
                throw new InvalidOperationException("Only announcements, project updates and account messages can be sent manually.");

            var requestKey = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.RequestKey), 100);
            if (requestKey != null)
            {
                var existing = await _context.NotificationJobs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.RequestKey == requestKey, cancellationToken);

                // A refresh, a retried request or an impatient second click all land here.
                if (existing != null)
                    return await MapJobAsync(existing, cancellationToken);
            }

            var preview = await PreviewAudienceAsync(dto, cancellationToken);

            if (preview.TotalRecipients == 0)
                throw new InvalidOperationException("That audience has no recipients.");

            if (preview.TotalRecipients > _options.MaxBroadcastRecipients)
                throw new InvalidOperationException(
                    $"That audience has {preview.TotalRecipients} recipients, above the {_options.MaxBroadcastRecipients} limit.");

            if (preview.RequiresConfirmation && !dto.ConfirmLargeAudience)
                throw new InvalidOperationException(
                    $"This will reach {preview.TotalRecipients} people. Confirm the audience before sending.");

            var now = _clock.GetUtcNow().UtcDateTime;
            DateTime? scheduledAt = null;

            if (dto.ScheduledAt.HasValue)
            {
                // The client sends an instant; storing it in UTC is what makes the schedule
                // behave the same wherever the server and the composer happen to be.
                var utc = dto.ScheduledAt.Value.Kind switch
                {
                    DateTimeKind.Utc => dto.ScheduledAt.Value,
                    DateTimeKind.Local => dto.ScheduledAt.Value.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(dto.ScheduledAt.Value, DateTimeKind.Utc)
                };

                if (utc > now.AddMinutes(1))
                {
                    if (utc > now.AddYears(1))
                        throw new InvalidOperationException("A notification cannot be scheduled more than a year ahead.");
                    scheduledAt = utc;
                }
            }

            var channels = NotificationChannel.InApp
                           | (dto.SendEmail ? NotificationChannel.Email : NotificationChannel.None)
                           | (dto.SendPush ? NotificationChannel.WebPush : NotificationChannel.None);

            var job = new NotificationJob
            {
                Status = NotificationJobStatus.Scheduled,
                Type = dto.Type,
                Category = NotificationCatalog.CategoryOf(dto.Type),
                Priority = dto.Priority,
                Title = LeadContactNormalizer.Limit(title, 200),
                Message = LeadContactNormalizer.Limit(message, 2000),
                ActionUrl = actionUrl,
                Channels = channels,
                AudienceType = dto.Audience.Type,
                AudienceJson = JsonSerializer.Serialize(ToSelection(dto.Audience)),
                ScheduledAt = scheduledAt,
                CreatedAt = now,
                CreatedByUserId = ctx.UserId,
                RecipientCount = preview.TotalRecipients,
                RequestKey = requestKey
            };

            _context.NotificationJobs.Add(job);
            _context.NotificationAuditEntries.Add(new NotificationAuditEntry
            {
                Area = "job",
                Action = scheduledAt.HasValue ? "schedule" : "send",
                Details = $"{job.Title} → {preview.Description} ({preview.TotalRecipients} recipient(s)) on {channels}.",
                PerformedByUserId = ctx.UserId,
                PerformedByName = LeadContactNormalizer.LimitOrNull(ctx.DisplayName, 200),
                OccurredAt = now
            });

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (NotificationDispatcher.IsDuplicateKey(ex) && requestKey != null)
            {
                // Two clicks arrived at once; the first one's job is the answer.
                _context.ChangeTracker.Clear();
                var winner = await _context.NotificationJobs.AsNoTracking()
                    .FirstAsync(j => j.RequestKey == requestKey, cancellationToken);
                return await MapJobAsync(winner, cancellationToken);
            }

            return await MapJobAsync(job, cancellationToken);
        }

        public async Task<List<NotificationJobDto>> GetJobsAsync(int take, CancellationToken cancellationToken = default)
        {
            var jobs = await _context.NotificationJobs
                .AsNoTracking()
                .OrderByDescending(j => j.ScheduledAt ?? j.CreatedAt)
                .ThenByDescending(j => j.Id)
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync(cancellationToken);

            var names = await ResolveNamesAsync(jobs.Select(j => (int?)j.CreatedByUserId), cancellationToken);
            return jobs.Select(j => MapJob(j, names)).ToList();
        }

        public async Task<NotificationJobDto> CancelJobAsync(
            int jobId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var job = await _context.NotificationJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
                      ?? throw new LeadNotFoundException("That scheduled notification was not found.");

            if (job.Status != NotificationJobStatus.Scheduled)
                throw new InvalidOperationException(
                    $"This notification is already {job.Status.ToString().ToLowerInvariant()} and can no longer be cancelled.");

            job.Status = NotificationJobStatus.Cancelled;
            job.CancelledAt = _clock.GetUtcNow().UtcDateTime;
            job.CancelledByUserId = ctx.UserId;
            job.LockedUntil = null;
            job.LockedBy = null;

            _context.NotificationAuditEntries.Add(new NotificationAuditEntry
            {
                Area = "job",
                Action = "cancel",
                Details = $"Cancelled '{job.Title}'.",
                EntityId = job.Id,
                PerformedByUserId = ctx.UserId,
                PerformedByName = LeadContactNormalizer.LimitOrNull(ctx.DisplayName, 200)
            });

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A worker claimed it in the same instant. Its send is already under way, so
                // the honest answer is that cancellation came too late.
                throw new InvalidOperationException("This notification started sending a moment ago and can no longer be cancelled.");
            }

            return await MapJobAsync(job, cancellationToken);
        }

        // ── Delivery history ────────────────────────────────────────────────────────

        public async Task<DeliveryHistoryPageDto> GetDeliveryHistoryAsync(
            DeliveryHistoryFilterDto filter, CancellationToken cancellationToken = default)
        {
            var query = _context.NotificationDeliveries
                .AsNoTracking()
                .Include(d => d.Notification)
                .AsQueryable();

            if (filter.Channel.HasValue)
                query = query.Where(d => d.Channel == filter.Channel.Value);

            if (filter.Status.HasValue)
                query = query.Where(d => d.Status == filter.Status.Value);

            if (filter.FailuresOnly)
                query = query.Where(d => d.Status == NotificationDeliveryStatus.Failed
                                         || d.Status == NotificationDeliveryStatus.Bounced
                                         || d.Status == NotificationDeliveryStatus.Unavailable
                                         || d.Status == NotificationDeliveryStatus.Expired);

            if (filter.Type.HasValue)
                query = query.Where(d => d.Notification.Type == filter.Type.Value);

            if (filter.Category.HasValue)
                query = query.Where(d => d.Notification.Category == filter.Category.Value);

            if (filter.RecipientUserId.HasValue)
                query = query.Where(d => d.Notification.RecipientUserId == filter.RecipientUserId.Value);

            if (filter.JobId.HasValue)
                query = query.Where(d => d.Notification.NotificationJobId == filter.JobId.Value);

            if (filter.From.HasValue)
                query = query.Where(d => d.Notification.CreatedAt >= filter.From.Value);

            if (filter.To.HasValue)
                query = query.Where(d => d.Notification.CreatedAt <= filter.To.Value);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.Trim().ToLower();
                query = query.Where(d => d.Notification.Title.ToLower().Contains(term)
                                         || (d.Target != null && d.Target.ToLower().Contains(term))
                                         || (d.FailureReason != null && d.FailureReason.ToLower().Contains(term)));
            }

            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize is < 1 or > 100 ? 25 : filter.PageSize;

            var totalCount = await query.CountAsync(cancellationToken);

            var rows = await query
                .OrderByDescending(d => d.Notification.CreatedAt)
                .ThenByDescending(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    d.Id,
                    d.NotificationId,
                    d.Channel,
                    d.Status,
                    d.Target,
                    d.AttemptCount,
                    d.AvailableAt,
                    d.ProcessingStartedAt,
                    d.SentAt,
                    d.DeliveredAt,
                    d.FailedAt,
                    d.ProviderReference,
                    d.FailureReason,
                    d.IsPermanentFailure,
                    d.Notification.Type,
                    d.Notification.Category,
                    d.Notification.Title,
                    d.Notification.CreatedAt,
                    d.Notification.EntityType,
                    d.Notification.EntityId,
                    d.Notification.RecipientUserId,
                    // A contact-only notification has no account to name; the stored contact
                    // name is what the admin sees instead.
                    RecipientName = d.Notification.Recipient != null
                        ? d.Notification.Recipient.FullName
                        : d.Notification.RecipientName,
                    JobId = d.Notification.NotificationJobId,
                    d.Notification.CreatedByUserId
                })
                .ToListAsync(cancellationToken);

            var names = await ResolveNamesAsync(rows.Select(r => r.CreatedByUserId), cancellationToken);

            var items = rows.Select(r => new DeliveryHistoryItemDto
            {
                Id = r.Id,
                NotificationId = r.NotificationId,
                Type = r.Type,
                Category = r.Category,
                Title = r.Title,
                Channel = r.Channel,
                Status = r.Status,
                RecipientUserId = r.RecipientUserId,
                RecipientName = r.RecipientName,
                Target = r.Target,
                EntityType = r.EntityType,
                EntityId = r.EntityId,
                CreatedAt = r.CreatedAt,
                ScheduledFor = r.AvailableAt,
                ProcessingStartedAt = r.ProcessingStartedAt,
                SentAt = r.SentAt,
                DeliveredAt = r.DeliveredAt,
                FailedAt = r.FailedAt,
                AttemptCount = r.AttemptCount,
                NextAttemptAt = r.Status == NotificationDeliveryStatus.Retrying ? r.AvailableAt : null,
                ProviderReference = r.ProviderReference,
                FailureReason = r.FailureReason,
                IsPermanentFailure = r.IsPermanentFailure,
                // A retry only makes sense for something that has stopped and did not succeed.
                CanRetry = r.Channel != NotificationChannel.InApp
                           && r.Status is NotificationDeliveryStatus.Failed
                                        or NotificationDeliveryStatus.Bounced
                                        or NotificationDeliveryStatus.Unavailable
                                        or NotificationDeliveryStatus.Expired
                                        or NotificationDeliveryStatus.Skipped,
                JobId = r.JobId,
                CreatedByName = r.CreatedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null
            }).ToList();

            return new DeliveryHistoryPageDto
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<DeliveryHistoryItemDto> RetryDeliveryAsync(
            int deliveryId, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var delivery = await _context.NotificationDeliveries
                .Include(d => d.Notification)
                .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken)
                ?? throw new LeadNotFoundException("That delivery was not found.");

            if (delivery.Status is NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.Delivered)
                throw new InvalidOperationException("This message was already sent; retrying would deliver it twice.");

            if (delivery.Status is NotificationDeliveryStatus.Pending
                or NotificationDeliveryStatus.Retrying
                or NotificationDeliveryStatus.Processing)
                throw new InvalidOperationException("This message is already queued to be sent.");

            var now = _clock.GetUtcNow().UtcDateTime;

            if (delivery.Channel == NotificationChannel.Email)
            {
                // A retry after an address has been fixed should not be blocked by the
                // suppression the bounce created.
                string? address = delivery.Notification.RecipientEmail;
                if (delivery.Notification.RecipientUserId is > 0)
                {
                    var loginEmail = await _context.Users
                        .AsNoTracking()
                        .Where(u => u.UserId == delivery.Notification.RecipientUserId)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync(cancellationToken);

                    var customerEmail = await _context.Customers
                        .AsNoTracking()
                        .Where(c => c.UserId == delivery.Notification.RecipientUserId
                                    && c.Email != null && c.Email != "")
                        .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                        .Select(c => c.Email)
                        .FirstOrDefaultAsync(cancellationToken);

                    address = SmtpEmailSender.IsValidAddress(customerEmail)
                        ? customerEmail
                        : loginEmail;
                }

                if (!string.IsNullOrWhiteSpace(address))
                {
                    var normalized = address.Trim().ToLowerInvariant();
                    var suppression = await _context.EmailSuppressions
                        .FirstOrDefaultAsync(s => s.Email == normalized && s.ClearedAt == null, cancellationToken);

                    if (suppression != null)
                    {
                        suppression.ClearedAt = now;
                        suppression.ClearedByUserId = ctx.UserId;
                    }
                }
            }

            delivery.Status = NotificationDeliveryStatus.Pending;
            delivery.AvailableAt = now;
            delivery.AttemptCount = 0;
            delivery.IsPermanentFailure = false;
            delivery.FailureReason = null;
            delivery.FailedAt = null;
            delivery.LockedUntil = null;
            delivery.LockedBy = null;

            // The notification is being sent again on purpose; do not let a stale expiry stop
            // the worker from picking it up.
            if (delivery.Notification.ExpiresAt.HasValue && delivery.Notification.ExpiresAt <= now)
                delivery.Notification.ExpiresAt = now.AddDays(1);

            _context.NotificationAuditEntries.Add(new NotificationAuditEntry
            {
                Area = "delivery",
                Action = "retry",
                Details = $"Requeued {delivery.Channel} delivery for '{delivery.Notification.Title}'.",
                EntityId = delivery.Id,
                PerformedByUserId = ctx.UserId,
                PerformedByName = LeadContactNormalizer.LimitOrNull(ctx.DisplayName, 200)
            });

            await _context.SaveChangesAsync(cancellationToken);

            return new DeliveryHistoryItemDto
            {
                Id = delivery.Id,
                NotificationId = delivery.NotificationId,
                Type = delivery.Notification.Type,
                Category = delivery.Notification.Category,
                Title = delivery.Notification.Title,
                Channel = delivery.Channel,
                Status = delivery.Status,
                RecipientUserId = delivery.Notification.RecipientUserId,
                Target = delivery.Target,
                EntityType = delivery.Notification.EntityType,
                EntityId = delivery.Notification.EntityId,
                CreatedAt = delivery.Notification.CreatedAt,
                ScheduledFor = delivery.AvailableAt,
                AttemptCount = delivery.AttemptCount,
                JobId = delivery.Notification.NotificationJobId,
                CanRetry = false
            };
        }

        // ── Suppressions ────────────────────────────────────────────────────────────

        public async Task<List<EmailSuppressionDto>> GetSuppressionsAsync(CancellationToken cancellationToken = default) =>
            await _context.EmailSuppressions
                .AsNoTracking()
                .Where(s => s.ClearedAt == null)
                .OrderByDescending(s => s.CreatedAt)
                .Take(200)
                .Select(s => new EmailSuppressionDto
                {
                    Id = s.Id,
                    Email = s.Email,
                    Reason = s.Reason,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync(cancellationToken);

        public async Task RemoveSuppressionAsync(int id, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var suppression = await _context.EmailSuppressions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                              ?? throw new LeadNotFoundException("That suppressed address was not found.");

            suppression.ClearedAt = _clock.GetUtcNow().UtcDateTime;
            suppression.ClearedByUserId = ctx.UserId;

            _context.NotificationAuditEntries.Add(new NotificationAuditEntry
            {
                Area = "suppression",
                Action = "clear",
                Details = $"Cleared the block on {EmailChannelSender.Mask(suppression.Email)}.",
                EntityId = suppression.Id,
                PerformedByUserId = ctx.UserId,
                PerformedByName = LeadContactNormalizer.LimitOrNull(ctx.DisplayName, 200)
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static NotificationAudienceSelection ToSelection(NotificationAudienceDto dto) => new()
        {
            UserIds = dto.UserIds.Distinct().Take(5000).ToList(),
            TeamId = dto.TeamId,
            ProjectId = dto.ProjectId,
            BookingIds = dto.BookingIds.Distinct().Take(5000).ToList(),
            LeadIds = dto.LeadIds.Distinct().Take(5000).ToList()
        };

        private async Task<NotificationJobDto> MapJobAsync(NotificationJob job, CancellationToken cancellationToken)
        {
            var names = await ResolveNamesAsync(new int?[] { job.CreatedByUserId }, cancellationToken);
            return MapJob(job, names);
        }

        private NotificationJobDto MapJob(NotificationJob job, IReadOnlyDictionary<int, string> names)
        {
            NotificationAudienceSelection selection;
            try
            {
                selection = string.IsNullOrWhiteSpace(job.AudienceJson)
                    ? new NotificationAudienceSelection()
                    : JsonSerializer.Deserialize<NotificationAudienceSelection>(job.AudienceJson) ?? new NotificationAudienceSelection();
            }
            catch (JsonException)
            {
                selection = new NotificationAudienceSelection();
            }

            return new NotificationJobDto
            {
                Id = job.Id,
                Status = job.Status,
                Type = job.Type,
                Category = job.Category,
                Priority = job.Priority,
                Title = job.Title,
                Message = job.Message,
                Channels = job.Channels,
                AudienceType = job.AudienceType,
                AudienceDescription = _recipients.Describe(job.AudienceType, selection),
                ScheduledAt = job.ScheduledAt,
                CreatedAt = job.CreatedAt,
                CreatedByName = names.TryGetValue(job.CreatedByUserId, out var name) ? name : null,
                CompletedAt = job.CompletedAt,
                CancelledAt = job.CancelledAt,
                RecipientCount = job.RecipientCount,
                FailureReason = job.FailureReason,
                ActionUrl = job.ActionUrl
            };
        }

        private async Task<Dictionary<int, string>> ResolveNamesAsync(IEnumerable<int?> userIds, CancellationToken cancellationToken)
        {
            var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<int, string>();

            return await _context.Users
                .AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        }
    }
}
