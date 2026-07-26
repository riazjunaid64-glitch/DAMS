using System.Globalization;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// The bridge between DAMS business events and the notification platform.
    ///
    /// Everything here is idempotent and is called <em>after</em> the business transaction has
    /// committed, so an email server being down can never roll back a payment. The price of
    /// that separation is a window where a payment is committed but its notification is not:
    /// <see cref="ReconcilePaymentReceiptsAsync"/> closes it, which is why a crash between the
    /// two loses nothing.
    /// </summary>
    public sealed class NotificationEventService : INotificationEventService
    {
        private readonly AppDbContext _context;
        private readonly INotificationDispatcher _dispatcher;
        private readonly NotificationSettingsStore _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<NotificationEventService> _logger;
        private bool? _schemaAvailable;

        public NotificationEventService(
            AppDbContext context,
            INotificationDispatcher dispatcher,
            NotificationSettingsStore settings,
            TimeProvider clock,
            ILogger<NotificationEventService> logger)
        {
            _context = context;
            _dispatcher = dispatcher;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // ── Payments ────────────────────────────────────────────────────────────────

        public async Task NotifyPaymentRecordedAsync(int paymentId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var payment = await _context.Payments
                .AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => new
                {
                    p.Id,
                    p.Amount,
                    p.PaidAt,
                    p.ReceiptNumber,
                    p.PaymentMethod,
                    p.BookingId,
                    p.Booking.BookingReference,
                    CustomerId = p.Booking.CustomerId,
                    CustomerName = p.Booking.Customer.FullName,
                    CustomerUserId = p.Booking.Customer.UserId,
                    CustomerEmail = p.Booking.Customer.Email,
                    ProjectName = p.Booking.Unit.Project.ProjectName,
                    UnitNumber = p.Booking.Unit.UnitNumber,
                    InstallmentSequence = p.Installment != null ? (int?)p.Installment.SequenceNumber : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (payment == null)
                return;

            var branding = await _settings.GetBrandingAsync(cancellationToken);

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customerName"] = payment.CustomerName,
                // Formatted from the payment row itself; the notification platform never
                // recomputes a financial figure.
                ["amount"] = $"{branding.CurrencySymbol} {payment.Amount.ToString("N2", CultureInfo.InvariantCulture)}",
                ["paymentDate"] = payment.PaidAt.ToString(branding.DateFormat, CultureInfo.InvariantCulture),
                ["receiptNumber"] = payment.ReceiptNumber,
                ["bookingReference"] = payment.BookingReference,
                ["projectName"] = payment.ProjectName,
                ["unitNumber"] = payment.UnitNumber,
                ["paymentMethod"] = payment.PaymentMethod.ToString(),
                ["installmentNumber"] = payment.InstallmentSequence?.ToString(CultureInfo.InvariantCulture)
            };

            var receiptLabel = string.IsNullOrWhiteSpace(payment.ReceiptNumber)
                ? $"payment #{payment.Id}"
                : payment.ReceiptNumber;

            await DispatchSafelyAsync(new NotificationRequest
            {
                Type = NotificationType.PaymentReceipt,
                RecipientUserId = payment.CustomerUserId,
                RecipientEmail = payment.CustomerUserId is > 0 ? null : payment.CustomerEmail,
                RecipientName = payment.CustomerName,
                // One receipt per payment per recipient, whatever causes the repeat: a
                // double-click, a refresh, a worker retry or the reconciliation sweep.
                DedupKey = $"PaymentReceipt:{payment.Id}",
                Title = $"Payment received — {receiptLabel}",
                Message = $"We have received {data["amount"]} against booking {payment.BookingReference}.",
                EntityType = NotificationEntityType.Payment,
                EntityId = payment.Id,
                SecondaryEntityId = payment.BookingId,
                DeepLink = NotificationLink.ForCustomerReceipt(payment.BookingId, payment.Id),
                Data = data
            }, $"payment {payment.Id}", cancellationToken);
        }

        public async Task<int> ReconcilePaymentReceiptsAsync(int maxRows, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return 0;

            var missing = await _context.Payments
                .AsNoTracking()
                .Where(p => !_context.Notifications.Any(n =>
                                n.Type == NotificationType.PaymentReceipt
                                && n.EntityType == NotificationEntityType.Payment
                                && n.EntityId == p.Id))
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .Take(Math.Clamp(maxRows, 1, 200))
                .ToListAsync(cancellationToken);

            foreach (var paymentId in missing)
                await NotifyPaymentRecordedAsync(paymentId, cancellationToken);

            return missing.Count;
        }

        public async Task<int> ReconcileBusinessEventsAsync(
            int maxRows, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return 0;

            var rawCutoff = await _settings.GetAsync(NotificationSettingKeys.PlatformActivatedAt, cancellationToken);
            var cutoff = DateTime.TryParse(
                rawCutoff,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var activated)
                ? activated.ToUniversalTime()
                : DateTime.UnixEpoch;
            var remaining = Math.Clamp(maxRows, 1, 500);
            var recovered = 0;

            var tasks = await _context.EmployeeTasks
                .AsNoTracking()
                .Where(t => t.CreatedAt >= cutoff
                            && t.Status != EmployeeTaskStatus.Cancelled
                            && !_context.Notifications.Any(n =>
                                n.Type == NotificationType.EmployeeTaskAssigned
                                && n.EntityType == NotificationEntityType.EmployeeTask
                                && n.EntityId == t.Id))
                .OrderBy(t => t.Id)
                .Select(t => t.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);
            foreach (var id in tasks)
            {
                await NotifyEmployeeTaskAssignedAsync(id, null, cancellationToken);
                recovered++;
            }
            remaining -= tasks.Count;
            if (remaining <= 0)
                return recovered;

            var requests = await _context.BookingRequests
                .AsNoTracking()
                .Where(r => r.CreatedAt >= cutoff
                            && !_context.Notifications.Any(n =>
                                n.Type == NotificationType.BookingRequestReceived
                                && n.EntityType == NotificationEntityType.BookingRequest
                                && n.EntityId == r.Id))
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);
            foreach (var id in requests)
            {
                await NotifyBookingRequestReceivedAsync(id, cancellationToken);
                recovered++;
            }
            remaining -= requests.Count;
            if (remaining <= 0)
                return recovered;

            var approvedBookings = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.CreatedAt >= cutoff
                            && b.BookingRequestId != null
                            && b.BookingRequest!.Status == BookingRequestStatus.Approved
                            && !_context.Notifications.Any(n =>
                                n.Type == NotificationType.BookingApproved
                                && n.EntityType == NotificationEntityType.Booking
                                && n.EntityId == b.Id))
                .OrderBy(b => b.Id)
                .Select(b => b.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);
            foreach (var id in approvedBookings)
            {
                await NotifyBookingStatusAsync(id, NotificationType.BookingApproved, null, null, cancellationToken);
                recovered++;
            }
            remaining -= approvedBookings.Count;
            if (remaining <= 0)
                return recovered;

            var rejected = await _context.BookingRequests
                .AsNoTracking()
                .Where(r => r.CreatedAt >= cutoff
                            && r.Status == BookingRequestStatus.Rejected
                            && !_context.Notifications.Any(n =>
                                n.Type == NotificationType.BookingRejected
                                && n.EntityType == NotificationEntityType.BookingRequest
                                && n.EntityId == r.Id))
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.RejectionReason })
                .Take(remaining)
                .ToListAsync(cancellationToken);
            foreach (var request in rejected)
            {
                await NotifyBookingRequestRejectedAsync(request.Id, request.RejectionReason, null, cancellationToken);
                recovered++;
            }
            remaining -= rejected.Count;
            if (remaining <= 0)
                return recovered;

            var bookings = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.CreatedAt >= cutoff
                            && ((b.Status == BookingStatus.Cancelled
                                 && !_context.Notifications.Any(n =>
                                     n.Type == NotificationType.BookingCancelled
                                     && n.EntityType == NotificationEntityType.Booking
                                     && n.EntityId == b.Id))
                                || (b.Status == BookingStatus.PossessionGiven
                                    && !_context.Notifications.Any(n =>
                                        n.Type == NotificationType.PossessionGiven
                                        && n.EntityType == NotificationEntityType.Booking
                                        && n.EntityId == b.Id))
                                || (b.Status == BookingStatus.SaleCompleted
                                    && !_context.Notifications.Any(n =>
                                        n.Type == NotificationType.SaleCompleted
                                        && n.EntityType == NotificationEntityType.Booking
                                        && n.EntityId == b.Id))))
                .OrderBy(b => b.Id)
                .Select(b => new { b.Id, b.Status })
                .Take(remaining)
                .ToListAsync(cancellationToken);

            foreach (var booking in bookings)
            {
                var type = booking.Status switch
                {
                    BookingStatus.Cancelled => NotificationType.BookingCancelled,
                    BookingStatus.PossessionGiven => NotificationType.PossessionGiven,
                    BookingStatus.SaleCompleted => NotificationType.SaleCompleted,
                    _ => NotificationType.AdminAnnouncement
                };
                await NotifyBookingStatusAsync(booking.Id, type, null, null, cancellationToken);
                recovered++;
            }

            return recovered;
        }

        // ── Bookings ────────────────────────────────────────────────────────────────

        public async Task NotifyBookingStatusAsync(
            int bookingId, NotificationType type, string? reason, int? actorUserId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var booking = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Id == bookingId)
                .Select(b => new
                {
                    b.Id,
                    b.BookingReference,
                    b.Status,
                    CustomerName = b.Customer.FullName,
                    CustomerUserId = b.Customer.UserId,
                    CustomerEmail = b.Customer.Email,
                    ProjectName = b.Unit.Project.ProjectName,
                    UnitNumber = b.Unit.UnitNumber
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (booking == null)
                return;

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customerName"] = booking.CustomerName,
                ["bookingReference"] = booking.BookingReference,
                ["projectName"] = booking.ProjectName,
                ["unitNumber"] = booking.UnitNumber,
                ["status"] = booking.Status.ToString(),
                ["reason"] = LeadContactNormalizer.Clean(reason)
            };

            var title = type switch
            {
                NotificationType.BookingApproved => $"Booking {booking.BookingReference} approved",
                NotificationType.BookingRejected => $"Update on your request for {booking.UnitNumber}",
                NotificationType.BookingCancelled => $"Booking {booking.BookingReference} cancelled",
                NotificationType.PossessionGiven => $"Possession handed over for {booking.UnitNumber}",
                NotificationType.SaleCompleted => $"Sale completed for {booking.UnitNumber}",
                _ => $"Update on booking {booking.BookingReference}"
            };

            await DispatchSafelyAsync(new NotificationRequest
            {
                Type = type,
                RecipientUserId = booking.CustomerUserId,
                RecipientEmail = booking.CustomerUserId is > 0 ? null : booking.CustomerEmail,
                RecipientName = booking.CustomerName,
                DedupKey = $"{type}:booking:{booking.Id}",
                Title = title,
                Message = string.IsNullOrWhiteSpace(reason)
                    ? $"{booking.UnitNumber} at {booking.ProjectName}."
                    : $"{booking.UnitNumber} at {booking.ProjectName}. {reason.Trim()}",
                EntityType = NotificationEntityType.Booking,
                EntityId = booking.Id,
                DeepLink = NotificationLink.ForCustomerBooking(booking.Id),
                CreatedByUserId = actorUserId,
                Data = data
            }, $"booking {booking.Id}", cancellationToken);
        }

        public async Task NotifyBookingRequestReceivedAsync(int bookingRequestId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var request = await _context.BookingRequests
                .AsNoTracking()
                .Where(r => r.Id == bookingRequestId)
                .Select(r => new
                {
                    r.Id,
                    r.FullName,
                    r.Email,
                    r.UserId,
                    ProjectName = r.Unit.Project.ProjectName,
                    UnitNumber = r.Unit.UnitNumber
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (request == null)
                return;

            await DispatchSafelyAsync(new NotificationRequest
            {
                Type = NotificationType.BookingRequestReceived,
                RecipientUserId = request.UserId,
                RecipientEmail = request.UserId is > 0 ? null : request.Email,
                RecipientName = request.FullName,
                DedupKey = $"BookingRequestReceived:{request.Id}",
                Title = "We have received your inquiry",
                Message = $"Thank you for your interest in {request.UnitNumber} at {request.ProjectName}. Our team will contact you shortly.",
                EntityType = NotificationEntityType.BookingRequest,
                EntityId = request.Id,
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["customerName"] = request.FullName,
                    ["projectName"] = request.ProjectName,
                    ["unitNumber"] = request.UnitNumber
                }
            }, $"booking request {request.Id}", cancellationToken);
        }

        public async Task NotifyBookingRequestRejectedAsync(
            int bookingRequestId, string? reason, int? actorUserId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var request = await _context.BookingRequests
                .AsNoTracking()
                .Where(r => r.Id == bookingRequestId)
                .Select(r => new
                {
                    r.Id,
                    r.FullName,
                    r.Email,
                    r.UserId,
                    ProjectName = r.Unit.Project.ProjectName,
                    UnitNumber = r.Unit.UnitNumber
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (request == null)
                return;

            var cleanReason = LeadContactNormalizer.Clean(reason);

            await DispatchSafelyAsync(new NotificationRequest
            {
                Type = NotificationType.BookingRejected,
                RecipientUserId = request.UserId,
                RecipientEmail = request.UserId is > 0 ? null : request.Email,
                RecipientName = request.FullName,
                DedupKey = $"BookingRejected:request:{request.Id}",
                Title = $"Update on your request for {request.UnitNumber}",
                Message = cleanReason == null
                    ? $"Your request for {request.UnitNumber} at {request.ProjectName} could not be approved."
                    : $"Your request for {request.UnitNumber} at {request.ProjectName} could not be approved. {cleanReason}",
                EntityType = NotificationEntityType.BookingRequest,
                EntityId = request.Id,
                CreatedByUserId = actorUserId,
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["customerName"] = request.FullName,
                    ["projectName"] = request.ProjectName,
                    ["unitNumber"] = request.UnitNumber,
                    ["reason"] = cleanReason,
                    ["status"] = "Rejected"
                }
            }, $"booking request {request.Id}", cancellationToken);
        }

        // ── Employees and projects ──────────────────────────────────────────────────

        public async Task NotifyEmployeeTaskAssignedAsync(int taskId, int? actorUserId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var task = await _context.EmployeeTasks
                .AsNoTracking()
                .Where(t => t.Id == taskId)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.DueDate,
                    t.Priority,
                    t.EmployeeId,
                    EmployeeName = t.Employee.FullName,
                    EmployeeUserId = t.Employee.UserId,
                    EmployeeStatus = t.Employee.Status,
                    ProjectName = t.Project != null ? t.Project.ProjectName : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (task?.EmployeeUserId is not > 0 || task.EmployeeStatus != EmployeeStatus.Active)
                return;

            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var due = task.DueDate?.ToString(branding.DateFormat, CultureInfo.InvariantCulture);

            await DispatchSafelyAsync(new NotificationRequest
            {
                Type = NotificationType.EmployeeTaskAssigned,
                RecipientUserId = task.EmployeeUserId,
                DedupKey = $"EmployeeTaskAssigned:{task.Id}:{task.EmployeeUserId}",
                Title = $"New task: {task.Title}",
                Message = due == null ? task.Title : $"{task.Title} — due {due}.",
                EntityType = NotificationEntityType.EmployeeTask,
                EntityId = task.Id,
                SecondaryEntityId = task.EmployeeId,
                CreatedByUserId = actorUserId,
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["employeeName"] = task.EmployeeName,
                    ["taskTitle"] = task.Title,
                    ["dueDate"] = due,
                    ["dueDateSuffix"] = due == null ? null : $", due {due}",
                    ["priority"] = task.Priority.ToString(),
                    ["projectName"] = task.ProjectName
                }
            }, $"employee task {task.Id}", cancellationToken);
        }

        public async Task NotifyProjectUpdatedAsync(
            int projectId, string summary, int? actorUserId, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return;

            var project = await _context.Projects
                .AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Id, p.ProjectName })
                .FirstOrDefaultAsync(cancellationToken);

            if (project == null)
                return;

            var cleanSummary = LeadContactNormalizer.Clean(summary) ?? "There is an update on this project.";

            var customers = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Unit.ProjectId == projectId
                            && b.Status != BookingStatus.Cancelled
                            && b.Customer.Status == CustomerStatus.Active)
                .Select(b => new { b.Customer.UserId, b.Customer.FullName, b.Customer.Email })
                .Distinct()
                .ToListAsync(cancellationToken);

            // A stable bucket so a repeated update with the same wording on the same day does
            // not notify the same customers twice. The digest must be deterministic across
            // restarts — string.GetHashCode is randomised per process and would silently
            // re-notify everybody after a deployment.
            var bucket = _clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd");
            var digest = StableDigest(cleanSummary);

            foreach (var customer in customers)
            {
                var key = customer.UserId is > 0 ? $"u{customer.UserId}" : $"e{customer.Email?.ToLowerInvariant()}";

                await DispatchSafelyAsync(new NotificationRequest
                {
                    Type = NotificationType.ProjectUpdated,
                    RecipientUserId = customer.UserId,
                    RecipientEmail = customer.UserId is > 0 ? null : customer.Email,
                    RecipientName = customer.FullName,
                    DedupKey = $"ProjectUpdated:{projectId}:{key}:{bucket}:{digest}",
                    Title = $"Update on {project.ProjectName}",
                    Message = cleanSummary,
                    EntityType = NotificationEntityType.Project,
                    EntityId = project.Id,
                    CreatedByUserId = actorUserId,
                    Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["customerName"] = customer.FullName,
                        ["projectName"] = project.ProjectName,
                        ["updateSummary"] = cleanSummary
                    }
                }, $"project {projectId}", cancellationToken);
            }
        }

        // ── Installment reminders ───────────────────────────────────────────────────

        public async Task<int> RunInstallmentRemindersAsync(int maxRows, CancellationToken cancellationToken = default)
        {
            if (!await NotificationSchemaExistsAsync(cancellationToken))
                return 0;

            var rules = await _context.NotificationRules
                .AsNoTracking()
                .Where(r => r.Type == NotificationType.InstallmentDue || r.Type == NotificationType.InstallmentOverdue)
                .ToDictionaryAsync(r => r.Type, cancellationToken);

            rules.TryGetValue(NotificationType.InstallmentDue, out var dueRule);
            rules.TryGetValue(NotificationType.InstallmentOverdue, out var overdueRule);

            var dueEnabled = dueRule?.IsEnabled ?? true;
            var overdueEnabled = overdueRule?.IsEnabled ?? true;

            if (!dueEnabled && !overdueEnabled)
                return 0;

            var branding = await _settings.GetBrandingAsync(cancellationToken);
            // Installments are business dates, so "due" and "overdue" are judged in Pakistan
            // local time exactly as the finance module judges them.
            var today = PakistanTime.Today;
            var leadDays = dueRule?.ReminderLeadDays ?? NotificationConfigurationService.DefaultLeadDays(NotificationType.InstallmentDue);
            var horizon = today.AddDays(Math.Max(leadDays, 0));
            var take = Math.Clamp(maxRows, 1, 500);
            var overdueRepeats = overdueRule?.RepeatWhenOverdue ?? true;
            var overduePrefix = NotificationType.InstallmentOverdue + ":";
            var overdueSuffix = overdueRepeats ? $":{today:yyyy-MM-dd}" : string.Empty;
            var duePrefix = NotificationType.InstallmentDue + ":";

            var candidates = await _context.Installments
                .AsNoTracking()
                .Where(i => i.Status != InstallmentStatus.Paid
                            && i.Booking.Status != BookingStatus.Cancelled
                            && i.Booking.Customer.Status == CustomerStatus.Active
                            && i.DueDate <= horizon
                            && (i.DueDate < today
                                ? !_context.Notifications.Any(n =>
                                    n.DedupKey == overduePrefix + i.Id + overdueSuffix)
                                : !_context.Notifications.Any(n =>
                                    n.DedupKey == duePrefix + i.Id)))
                .OrderBy(i => i.DueDate)
                .Take(take)
                .Select(i => new
                {
                    i.Id,
                    i.SequenceNumber,
                    i.Amount,
                    i.DueDate,
                    i.BookingId,
                    i.Booking.BookingReference,
                    CustomerName = i.Booking.Customer.FullName,
                    CustomerUserId = i.Booking.Customer.UserId,
                    CustomerEmail = i.Booking.Customer.Email,
                    ProjectName = i.Booking.Unit.Project.ProjectName,
                    UnitNumber = i.Booking.Unit.UnitNumber
                })
                .ToListAsync(cancellationToken);

            var created = 0;

            foreach (var installment in candidates)
            {
                var overdue = installment.DueDate.Date < today;
                if (overdue ? !overdueEnabled : !dueEnabled)
                    continue;

                if (!overdue && !(dueRule?.RemindOnDueDate ?? true) && installment.DueDate.Date == today)
                    continue;

                var amount = $"{branding.CurrencySymbol} {installment.Amount.ToString("N2", CultureInfo.InvariantCulture)}";
                var dueText = installment.DueDate.ToString(branding.DateFormat, CultureInfo.InvariantCulture);
                var type = overdue ? NotificationType.InstallmentOverdue : NotificationType.InstallmentDue;

                // A due reminder fires once per installment. An overdue one may repeat, but
                // only once a day and only when the admin asked it to.
                var bucket = overdue && overdueRepeats
                    ? $":{today:yyyy-MM-dd}"
                    : string.Empty;

                var dispatched = await DispatchSafelyAsync(new NotificationRequest
                {
                    Type = type,
                    RecipientUserId = installment.CustomerUserId,
                    RecipientEmail = installment.CustomerUserId is > 0 ? null : installment.CustomerEmail,
                    RecipientName = installment.CustomerName,
                    DedupKey = $"{type}:{installment.Id}{bucket}",
                    Title = overdue
                        ? $"Installment {amount} is overdue"
                        : $"Installment {amount} due on {dueText}",
                    Message = $"Installment {installment.SequenceNumber} for booking {installment.BookingReference} " +
                              (overdue ? $"was due on {dueText}." : $"is due on {dueText}."),
                    EntityType = NotificationEntityType.Installment,
                    EntityId = installment.Id,
                    SecondaryEntityId = installment.BookingId,
                    DeepLink = NotificationLink.ForCustomerBooking(installment.BookingId),
                    Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["customerName"] = installment.CustomerName,
                        ["installmentAmount"] = amount,
                        ["installmentNumber"] = installment.SequenceNumber.ToString(CultureInfo.InvariantCulture),
                        ["dueDate"] = dueText,
                        ["bookingReference"] = installment.BookingReference,
                        ["projectName"] = installment.ProjectName,
                        ["unitNumber"] = installment.UnitNumber
                    }
                }, $"installment {installment.Id}", cancellationToken);

                if (dispatched)
                    created++;
            }

            return created;
        }

        /// <summary>FNV-1a: short, stable across processes and machines, and good enough to
        /// tell one wording apart from another inside a dedup key.</summary>
        private static string StableDigest(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in value)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash.ToString("x8");
            }
        }

        /// <summary>
        /// Dispatches without ever letting a notification problem escape into the business
        /// operation that raised it. A failure here is logged and left for the reconciliation
        /// sweep; it must never surface as a failed payment or a failed booking.
        /// </summary>
        private async Task<bool> DispatchSafelyAsync(NotificationRequest request, string context, CancellationToken cancellationToken)
        {
            try
            {
                return await _dispatcher.DispatchAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The notification for {Context} could not be created. The business record is unaffected.", context);
                return false;
            }
        }

        private async Task<bool> NotificationSchemaExistsAsync(CancellationToken cancellationToken)
        {
            if (_schemaAvailable.HasValue)
                return _schemaAvailable.Value;

            try
            {
                var connection = _context.Database.GetDbConnection();
                await _context.Database.OpenConnectionAsync(cancellationToken);
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT CASE WHEN
                            OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NOT NULL AND
                            OBJECT_ID(N'[dbo].[NotificationDeliveries]', N'U') IS NOT NULL AND
                            OBJECT_ID(N'[dbo].[NotificationRules]', N'U') IS NOT NULL AND
                            OBJECT_ID(N'[dbo].[NotificationSettings]', N'U') IS NOT NULL
                        THEN 1 ELSE 0 END
                        """;

                    var result = await command.ExecuteScalarAsync(cancellationToken);
                    _schemaAvailable = Convert.ToInt32(result) == 1;
                    return _schemaAvailable.Value;
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Notification event processing is paused because the notification schema check failed.");
                _schemaAvailable = false;
                return false;
            }
        }
    }
}
