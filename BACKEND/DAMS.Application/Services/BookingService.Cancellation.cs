using System.Data;
using DAMS.Application.Common;
using DAMS.Application.DTOs.BookingDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public partial class BookingService
    {
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string Required(string? value, string label, int max)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new InvalidOperationException($"{label} is required.");
            if (trimmed.Length > max) throw new InvalidOperationException($"{label} cannot exceed {max} characters.");
            return trimmed;
        }

        private static string? Limited(string? value, string label, int max)
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (trimmed?.Length > max) throw new InvalidOperationException($"{label} cannot exceed {max} characters.");
            return trimmed;
        }

        // Keeps the existing human-readable cancellation note for compatibility, but never at the
        // cost of truncating history: the structured settlement is the real source of truth, so
        // if the note would overflow the column it is simply skipped rather than cut short.
        private static string? AppendNoteIfItFits(string? existing, string note, int max = 1000)
        {
            var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC] {note}";
            var combined = string.IsNullOrWhiteSpace(existing) ? entry : $"{existing}\n{entry}";
            return combined.Length > max ? existing : combined;
        }

        /// <summary>Joins the caller's transaction if one is already open (so pay-refund calls
        /// made from a test harness or a future orchestrator compose cleanly); otherwise opens
        /// its own Serializable transaction via the execution strategy, matching the pattern
        /// already used by CommissionRebateService for money-moving operations.</summary>
        private async Task<T> SerializableAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            if (_context.Database.CurrentTransaction != null)
                return await operation();
            if (!_context.Database.IsRelational())
                return await operation();
            var strategy = _context.Database.CreateExecutionStrategy();
            var replaying = false;
            return await strategy.ExecuteAsync(async () =>
            {
                // A transient fault rolls the attempt back in the DATABASE and nowhere else, so the
                // retry would otherwise read the change tracker's copy of its own undone work and
                // decide against that. Each replay starts from the database instead.
                if (replaying) _context.ChangeTracker.Clear();
                replaying = true;
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var result = await operation();
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        private void ApplyBookingConcurrencyToken(Booking booking, string? token)
        {
            var property = _context.Entry(booking).Property(b => b.RowVersion);
            if (string.IsNullOrWhiteSpace(token))
            {
                if (property.CurrentValue is { Length: > 0 })
                    throw new InvalidOperationException("The booking version is missing. Refresh and try again.");
                return;
            }
            try { property.OriginalValue = Convert.FromBase64String(token); }
            catch (FormatException) { throw new InvalidOperationException("The booking version is invalid. Refresh and try again."); }
        }

        /// <summary>Blocks reusing a live outgoing payment reference on the same finance account,
        /// across every kind of outgoing financial transaction this account can carry — a
        /// cancellation refund, a commission payout, or a cash rebate disbursement.</summary>
        private async Task EnsureRefundReferenceAvailableAsync(int financeAccountId, string? paymentReference, CancellationToken cancellationToken)
        {
            if (paymentReference == null) return;
            var collides = await _context.BookingCancellationRefunds.AnyAsync(r =>
                    r.FinanceAccountId == financeAccountId && r.PaymentReference == paymentReference, cancellationToken)
                || await _context.CommissionPayouts.AnyAsync(p => p.FinanceAccountId == financeAccountId && p.PaymentReference == paymentReference
                    && p.Amount > p.Reversals.Sum(r => r.Amount), cancellationToken)
                || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == financeAccountId && d.Reference == paymentReference
                    && d.Amount > d.Reversals.Sum(r => r.Amount), cancellationToken);
            if (collides)
                throw new InvalidOperationException("This payment reference is already recorded against the selected finance account.");
        }

        public async Task<BookingResponseDto> CancelBookingAsync(int id, CancelBookingDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var (response, shouldNotify, reason) = await SerializableAsync(
                () => CancelBookingCoreAsync(id, dto, actor, cancellationToken), cancellationToken);

            // Best-effort, strictly after commit: nothing this does may undo money already saved.
            if (shouldNotify)
                await NotifyQuietlyAsync(n => n.NotifyBookingStatusAsync(id, NotificationType.BookingCancelled, reason, actor.UserId, cancellationToken));

            return response;
        }

        private async Task<(BookingResponseDto Response, bool ShouldNotify, string Reason)> CancelBookingCoreAsync(
            int id, CancelBookingDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken)
        {
            var reason = Required(dto.Reason, "Cancellation reason", 500);
            var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
            var refundAmount = Money(dto.RefundAmount);
            var expectedCash = Money(dto.ExpectedCustomerCashReceived);
            var paymentReferenceInput = Limited(dto.RefundPaymentReference, "Payment reference", 200);
            var notesInput = Limited(dto.RefundNotes, "Notes", 2000);

            // 2. Idempotent retry: an identical request with the same key returns the original
            // success; a reused key with a different payload is rejected outright.
            var existingByKey = await _context.BookingCancellationSettlements
                .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByKey != null)
            {
                // Comparing against the nullable dto value directly (not a defaulted local) means
                // a retry that omits RefundDecision can never match a settlement that recorded an
                // explicit one — an enum value is never equal to a missing one.
                var samePayload = existingByKey.BookingId == id
                    && existingByKey.CustomerCashReceivedSnapshot == expectedCash
                    && existingByKey.RefundAmount == refundAmount
                    && existingByKey.RefundDecision == dto.RefundDecision
                    && existingByKey.Reason == reason
                    && existingByKey.Notes == notesInput;
                if (samePayload && dto.RefundDecision == CancellationRefundDecision.PayNow)
                {
                    var existingRefund = await _context.BookingCancellationRefunds
                        .FirstOrDefaultAsync(r => r.SettlementId == existingByKey.Id, cancellationToken);
                    // Full-payload comparison: PaidAt matters just as much as the account, method
                    // and reference — a retry that silently changes it is a different request, not
                    // a duplicate, and must be rejected rather than accepted.
                    samePayload = existingRefund != null
                        && existingRefund.FinanceAccountId == dto.RefundFinanceAccountId
                        && existingRefund.PaymentMethod == dto.RefundPaymentMethod
                        && existingRefund.PaymentReference == paymentReferenceInput
                        && existingRefund.PaidAt == dto.RefundPaidAt;
                }
                else if (samePayload)
                {
                    // None/PayLater never carry payout details — a retry that suddenly does is a
                    // different request in disguise, not a duplicate of the original.
                    samePayload = !dto.RefundFinanceAccountId.HasValue && !dto.RefundPaymentMethod.HasValue
                        && paymentReferenceInput == null && !dto.RefundPaidAt.HasValue;
                }
                if (!samePayload)
                    throw new InvalidOperationException("This idempotency key was already used for a different cancellation.");
                return (await GetResponseAsync(id, cancellationToken), false, reason);
            }

            // 3. Load tracked Booking + Unit.
            var booking = await _context.Bookings.Include(b => b.Unit)
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Booking not found.");

            // 4. Booking concurrency token — protects against cancelling a booking that changed
            // (e.g. a new payment recorded) since the Admin opened the dialog.
            ApplyBookingConcurrencyToken(booking, dto.ConcurrencyToken);

            // 5. Status validation — unchanged from the existing rule.
            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("This booking has already been cancelled.");
            if (booking.Status is BookingStatus.PossessionGiven or BookingStatus.SaleCompleted)
                throw new InvalidOperationException("A booking that reached possession or completion cannot be cancelled here.");

            // 6. Authoritative customer cash received — the same SUM(Payment.Amount) definition
            // used everywhere else in DAMS, recomputed fresh rather than trusting the client.
            var actualCash = Money(await _context.Payments.Where(p => p.BookingId == id)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m);

            // 7. Stale-payment protection — the Admin's decision was made against a snapshot that
            // may no longer be true. Never silently recompute Retained around a moved target.
            if (actualCash != expectedCash)
                throw new InvalidOperationException(
                    "Customer payments changed while you were cancelling this booking. Reload and review the settlement again.");

            // 7.5. Explicit-decision safety invariant: once real money was received, a decision
            // must be actively chosen — an omitted RefundDecision must never silently become "no
            // refund". A booking with nothing paid has nothing to decide, so it may omit it.
            if (actualCash > 0m && !dto.RefundDecision.HasValue)
                throw new InvalidOperationException("Confirm the refund decision before continuing.");
            var refundDecision = dto.RefundDecision ?? CancellationRefundDecision.None;

            // One business-date snapshot for this whole operation. PakistanTime.Today must not be
            // read again after this point — the several awaits below (system-account resolution,
            // account validation, reference-collision checks) can straddle a Pakistan midnight,
            // and a second read could then disagree with this one, letting a PayNow refund's
            // PaidAt end up dated a day apart from the settlement's own CancellationDate.
            var businessDate = PakistanTime.Today;

            // 8. Refund amount / decision validation.
            if (refundAmount < 0m) throw new InvalidOperationException("Refund amount cannot be negative.");
            if (refundAmount > actualCash) throw new InvalidOperationException("Refund amount cannot exceed the customer's paid amount.");
            if (refundAmount == 0m)
            {
                if (refundDecision != CancellationRefundDecision.None)
                    throw new InvalidOperationException("Refund decision must be None when the refund amount is zero.");
                if (dto.RefundFinanceAccountId.HasValue || dto.RefundPaymentMethod.HasValue
                    || paymentReferenceInput != null || dto.RefundPaidAt.HasValue)
                    throw new InvalidOperationException("No payout details are needed when there is no refund.");
            }
            else if (refundDecision is not (CancellationRefundDecision.PayNow or CancellationRefundDecision.PayLater))
            {
                throw new InvalidOperationException("Choose whether the refund will be paid now or paid later.");
            }
            var retained = Money(actualCash - refundAmount);

            var payNow = refundAmount > 0m && refundDecision == CancellationRefundDecision.PayNow;
            if (refundDecision == CancellationRefundDecision.PayLater
                && (dto.RefundFinanceAccountId.HasValue || dto.RefundPaymentMethod.HasValue
                    || paymentReferenceInput != null || dto.RefundPaidAt.HasValue))
                throw new InvalidOperationException("A refund recorded as payable later must not include payout details.");

            string? paymentReference = null;
            if (payNow)
            {
                if (!dto.RefundFinanceAccountId.HasValue)
                    throw new InvalidOperationException("A refund source account is required when paying the refund now.");
                if (!dto.RefundPaymentMethod.HasValue || !Enum.IsDefined(dto.RefundPaymentMethod.Value))
                    throw new InvalidOperationException("Select a valid refund payment method.");
                paymentReference = paymentReferenceInput;
                if (dto.RefundPaymentMethod.Value != PaymentMethod.Cash && paymentReference == null)
                    throw new InvalidOperationException("A payment reference is required for a non-cash refund.");
                if (!dto.RefundPaidAt.HasValue)
                    throw new InvalidOperationException("A refund date is required when paying the refund now.");
                // The refund is being paid at cancellation time, so its business date can only be
                // "today" — never future-dated, and never before the cancellation it belongs to.
                if (dto.RefundPaidAt.Value.Date > businessDate)
                    throw new InvalidOperationException("Refund date cannot be in the future.");
                if (dto.RefundPaidAt.Value.Date < businessDate)
                    throw new InvalidOperationException("Refund date cannot be before the cancellation date.");
            }

            // 9. A refund obligation needs the Customer Refunds Payable liability to exist —
            // resolved/created here, inside this same transaction, so the Admin never has to set
            // it up by hand first.
            int? refundPayableAccountId = refundAmount > 0m
                ? await _accountService.EnsureSystemAccountAsync(FinanceSystemAccountRole.CustomerRefundPayable, cancellationToken)
                : null;

            // 10. PayNow: validate the payout source and block a duplicate reference.
            if (payNow)
            {
                await _accountService.EnsureSelectableAsync(dto.RefundFinanceAccountId!.Value, null, cancellationToken);
                await EnsureRefundReferenceAvailableAsync(dto.RefundFinanceAccountId.Value, paymentReference, cancellationToken);
            }

            var actorName = Limited(actor.DisplayName, "Actor name", 200) ?? "Admin";

            // 11. Create the settlement — the permanent record of the Admin's decision.
            var settlement = new BookingCancellationSettlement
            {
                BookingId = booking.Id,
                CustomerCashReceivedSnapshot = actualCash,
                RefundAmount = refundAmount,
                RetainedAmount = retained,
                RefundDecision = refundDecision,
                RefundPayableAccountId = refundPayableAccountId,
                Reason = reason,
                Notes = notesInput,
                IdempotencyKey = idempotencyKey,
                CancelledByUserId = actor.UserId,
                CancelledByName = actorName,
                CancelledAt = DateTime.UtcNow,
                // The Pakistan business date, not the raw UTC instant: a cancellation at 00:30 PKT
                // is still 19:30 UTC the previous calendar day, and every report must place this
                // settlement's retained income and refund liability on the PKT day the Admin actually acted.
                // Reused from the single snapshot taken above — never re-read — so it can never
                // disagree with the PayNow refund date validation a few awaits earlier.
                CancellationDate = businessDate
            };
            _context.BookingCancellationSettlements.Add(settlement);

            // 12. PayNow: the actual payout, created in the same transaction, amount pinned to
            // the settlement's decided RefundAmount.
            if (payNow)
            {
                _context.BookingCancellationRefunds.Add(new BookingCancellationRefund
                {
                    Settlement = settlement,
                    FinanceAccountId = dto.RefundFinanceAccountId!.Value,
                    Amount = refundAmount,
                    PaidAt = dto.RefundPaidAt!.Value,
                    PaymentMethod = dto.RefundPaymentMethod!.Value,
                    PaymentReference = paymentReference,
                    Notes = notesInput,
                    // Reused verbatim, not suffixed: the refund table's IdempotencyKey column has
                    // its own 80-char limit and its own unique index (table-scoped), so appending
                    // ":refund" to an already-80-char cancellation key would silently overflow it.
                    IdempotencyKey = idempotencyKey,
                    RecordedByUserId = actor.UserId,
                    RecordedByName = actorName,
                    RecordedAt = DateTime.UtcNow
                });
            }

            // 13/14. Cancel the booking and release the unit — unchanged from the existing rule:
            // the unit goes back to the market immediately, it does not wait on the refund.
            booking.Status = BookingStatus.Cancelled;
            booking.InternalNotes = AppendNoteIfItFits(booking.InternalNotes, $"Cancelled by {actorName}: {reason}");
            booking.UpdatedAt = DateTime.UtcNow;
            booking.Unit.Status = UnitStatus.Available;
            booking.Unit.UpdatedAt = DateTime.UtcNow;

            // 15. Existing commission/rebate cancellation lifecycle, reused verbatim and run
            // exactly once. Customer refund settlement and commission/rebate settlement are
            // different money flows and are never mixed.
            if (_commissionLifecycle != null)
                await _commissionLifecycle.HandleBookingCancelledAsync(booking.Id, reason, actor, cancellationToken);

            _context.FinancialWorkflowAuditEntries.Add(new FinancialWorkflowAuditEntry
            {
                BookingId = booking.Id, CustomerId = booking.CustomerId,
                Action = FinancialWorkflowAction.BookingCancellationSettlementRecorded,
                NewAmount = refundAmount, Reason = reason,
                PerformedByUserId = actor.UserId, PerformedByName = actorName, OccurredAt = DateTime.UtcNow
            });
            if (payNow)
                _context.FinancialWorkflowAuditEntries.Add(new FinancialWorkflowAuditEntry
                {
                    BookingId = booking.Id, CustomerId = booking.CustomerId,
                    Action = FinancialWorkflowAction.BookingCancellationRefundPaid,
                    NewAmount = refundAmount, Reason = paymentReference,
                    PerformedByUserId = actor.UserId, PerformedByName = actorName, OccurredAt = DateTime.UtcNow
                });

            // 16. Everything above is staged on one change tracker; one SaveChanges commits the
            // settlement, the optional refund, the booking/unit status, and the commission/rebate
            // lifecycle changes atomically. 17. The caller commits the transaction after this
            // returns — a failure anywhere above leaves nothing behind.
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // ApplyBookingConcurrencyToken's OriginalValue mismatch surfaces here as EF's raw
                // infrastructure exception. Translate it to the same clean, business-level error
                // every other InvalidOperationException in this method produces, so the controller
                // (and the frontend's stale-data handling) never has to special-case it.
                throw new InvalidOperationException(
                    "The booking changed while you were cancelling it. Refresh and review the settlement again.");
            }

            // 19. Freshly loaded, includes the settlement. 18 (notification) happens in the
            // caller, after the transaction actually commits.
            return (await GetResponseAsync(booking.Id, cancellationToken), true, reason);
        }

        public Task<BookingResponseDto> PayCancellationRefundAsync(int bookingId, PayCancellationRefundDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(() => PayCancellationRefundCoreAsync(bookingId, dto, actor, cancellationToken), cancellationToken);

        private async Task<BookingResponseDto> PayCancellationRefundCoreAsync(
            int bookingId, PayCancellationRefundDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken)
        {
            var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
            var paymentReferenceInput = Limited(dto.PaymentReference, "Payment reference", 200);
            var notesInput = Limited(dto.Notes, "Notes", 2000);

            // 1/2/3. Exact retry of an already-recorded payout returns the same success; a reused
            // key with a different payload is rejected.
            var existingByKey = await _context.BookingCancellationRefunds
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByKey != null)
            {
                var settlementForKey = await _context.BookingCancellationSettlements
                    .FirstAsync(s => s.Id == existingByKey.SettlementId, cancellationToken);
                // Full-payload comparison: PaidAt and Notes matter just as much as the account,
                // method and reference. PaidAt is only compared when the caller actually supplied
                // one — an omitted PaidAt is server-defaulted to "now" and can never be reproduced
                // byte-for-byte on a retry, so it is excluded rather than always mismatching.
                var samePayload = settlementForKey.BookingId == bookingId
                    && existingByKey.FinanceAccountId == dto.FinanceAccountId
                    && existingByKey.PaymentMethod == dto.PaymentMethod
                    && existingByKey.PaymentReference == paymentReferenceInput
                    && (!dto.PaidAt.HasValue || existingByKey.PaidAt == dto.PaidAt.Value)
                    && existingByKey.Notes == notesInput;
                if (!samePayload)
                    throw new InvalidOperationException("This idempotency key was already used for a different refund payment.");
                return await GetResponseAsync(bookingId, cancellationToken);
            }

            // 4/5. Load the booking; it must already be cancelled.
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                ?? throw new InvalidOperationException("Booking not found.");
            if (booking.Status != BookingStatus.Cancelled)
                throw new InvalidOperationException("This booking has not been cancelled.");

            // 6/7. A settlement with a positive refund must exist.
            var settlement = await _context.BookingCancellationSettlements
                .FirstOrDefaultAsync(s => s.BookingId == bookingId, cancellationToken)
                ?? throw new InvalidOperationException("No cancellation settlement was recorded for this booking.");
            if (settlement.RefundAmount <= 0m)
                throw new InvalidOperationException("This booking has no refund to pay.");

            // 8. At most one payout per settlement.
            if (await _context.BookingCancellationRefunds.AnyAsync(r => r.SettlementId == settlement.Id, cancellationToken))
                throw new InvalidOperationException("This cancellation refund has already been paid.");

            // 9/10/11. Validate the payout source, method, and non-cash reference requirement —
            // identical rules to the PayNow path at cancellation time.
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId, null, cancellationToken);
            if (!Enum.IsDefined(dto.PaymentMethod))
                throw new InvalidOperationException("Select a valid refund payment method.");
            var paymentReference = paymentReferenceInput;
            if (dto.PaymentMethod != PaymentMethod.Cash && paymentReference == null)
                throw new InvalidOperationException("A payment reference is required for a non-cash refund.");

            // One business-date snapshot, read once — matching the same rule CancelBookingCoreAsync
            // follows, so a future await inserted here can never make the default and the
            // future-date check disagree about what "today" was.
            var today = PakistanTime.Today;
            var paidAt = dto.PaidAt ?? today;

            // The liability was recognised on CancellationDate (the Pakistan business date, not the
            // raw UTC CancelledAt instant); the cash movement cannot predate that, and cannot be
            // future-dated while the system already reports the refund as Paid.
            if (paidAt.Date > today)
                throw new InvalidOperationException("Refund date cannot be in the future.");
            if (paidAt.Date < settlement.CancellationDate.Date)
                throw new InvalidOperationException("Refund date cannot be before the cancellation date.");

            // 12. Reference collision, scoped to the selected account.
            await EnsureRefundReferenceAvailableAsync(dto.FinanceAccountId, paymentReference, cancellationToken);

            var actorName = Limited(actor.DisplayName, "Actor name", 200) ?? "Admin";

            // 13. Amount is never accepted from the caller — it is always the already-decided
            // RefundAmount. This feature does not support partial payout of that obligation.
            _context.BookingCancellationRefunds.Add(new BookingCancellationRefund
            {
                SettlementId = settlement.Id,
                FinanceAccountId = dto.FinanceAccountId,
                Amount = settlement.RefundAmount,
                PaidAt = paidAt,
                PaymentMethod = dto.PaymentMethod,
                PaymentReference = paymentReference,
                Notes = notesInput,
                IdempotencyKey = idempotencyKey,
                RecordedByUserId = actor.UserId,
                RecordedByName = actorName,
                RecordedAt = DateTime.UtcNow
            });

            _context.FinancialWorkflowAuditEntries.Add(new FinancialWorkflowAuditEntry
            {
                BookingId = bookingId, CustomerId = booking.CustomerId,
                Action = FinancialWorkflowAction.BookingCancellationRefundPaid,
                NewAmount = settlement.RefundAmount, Reason = paymentReference,
                PerformedByUserId = actor.UserId, PerformedByName = actorName, OccurredAt = DateTime.UtcNow
            });

            // 14/15. The settlement itself is never modified — only the refund payout is created.
            await _context.SaveChangesAsync(cancellationToken);

            return await GetResponseAsync(bookingId, cancellationToken);
        }
    }
}
