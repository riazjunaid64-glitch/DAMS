using DAMS.Application.Common;
using DAMS.Application.DTOs.InstallmentDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class InstallmentService : IInstallmentService
    {
        private readonly AppDbContext _context;
        private readonly IFinanceAccountService _accountService;
        private readonly INotificationEventService? _notifications;

        /// <param name="notifications">
        /// Optional on purpose: recording an installment payment must not depend on the
        /// notification platform being present or healthy.
        /// </param>
        public InstallmentService(AppDbContext context, IFinanceAccountService accountService,
            INotificationEventService? notifications = null)
        {
            _context = context;
            _accountService = accountService;
            _notifications = notifications;
        }

        public async Task<InstallmentScheduleDto> GetScheduleAsync(int bookingId)
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            var canRegenerate = await CanRegenerateAsync(bookingId);
            var paidByInstallment = await GetPaidByInstallmentAsync(bookingId);
            var nonCashCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, bookingId);
            var stranded = await BookingCreditPolicy.UncollectableFromReversedCreditsAsync(
                _context, booking, booking.Installments.Sum(i => i.Amount), nonCashCredits);
            return MapSchedule(booking, canRegenerate, paidByInstallment, nonCashCredits, stranded);
        }

        public async Task<InstallmentScheduleDto> RecordInstallmentPaymentAsync(
            int bookingId, int installmentId, RecordInstallmentPaymentDto dto, int adminUserId)
        {
            if (dto.Amount <= 0m)
                throw new InvalidOperationException("Payment amount must be greater than zero.");
            if (!dto.FinanceAccountId.HasValue)
                throw new InvalidOperationException("Received In Account is required.");
            await _accountService.EnsureSelectableAsync(dto.FinanceAccountId.Value);

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot record a payment against a cancelled booking.");

            // Collection continues after possession. The sale is already recognised by then, so an
            // unpaid installment is no longer a promise — it is an Accounts Receivable balance,
            // and refusing the cash would leave a receivable that can never be cleared. The status
            // itself is deliberately not touched here: a PossessionGiven booking stays
            // PossessionGiven until it is legitimately completed.
            if (booking.Status is not (BookingStatus.PaymentPlanActive or BookingStatus.PossessionGiven))
                throw new InvalidOperationException(
                    "Installment payments can only be recorded while the payment plan is active or after possession.");

            var installment = await _context.Installments
                .FirstOrDefaultAsync(i => i.Id == installmentId && i.BookingId == bookingId);

            if (installment == null)
                throw new InvalidOperationException("Installment not found for this booking.");

            if (installment.Status == InstallmentStatus.Paid)
                throw new InvalidOperationException("This installment is already fully paid.");

            // The receipt date decides which month collected this money, and the whole balance model
            // assumes it is neither in the future nor before the committed opening balances. Nothing
            // filters movements to "up to today", so a post-dated receipt would reduce the customer's
            // outstanding balance — and potentially close the installment — the moment it was saved.
            var paidAt = await FinanceDateRules.ResolveInstantAsync(
                _context, dto.PaidAt, "Payment date", CancellationToken.None);

            var alreadyPaid = await _context.Payments
                .Where(p => p.InstallmentId == installmentId && p.Type == PaymentType.Installment)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            alreadyPaid += await ValidInstallmentCreditsAsync(installmentId);

            var remaining = installment.Amount - alreadyPaid;
            var totalCollected = await _context.Payments.Where(p => p.BookingId == bookingId)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var rebateCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, bookingId);
            await EnsureScheduleCoversTheBalanceAsync(booking, rebateCredits);
            var overallRemaining = Math.Max(0m, booking.AgreedSalePrice - booking.DiscountAmount - totalCollected - rebateCredits);
            remaining = Math.Min(remaining, overallRemaining);
            if (dto.Amount > remaining)
                throw new InvalidOperationException(
                    $"Payment exceeds the remaining installment balance. Remaining is {remaining:0.00}.");

            var payment = new Payment
            {
                BookingId = booking.Id,
                InstallmentId = installment.Id,
                FinanceAccountId = dto.FinanceAccountId,
                Type = PaymentType.Installment,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(dto.PaymentReference) ? null : dto.PaymentReference.Trim(),
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                RecordedByUserId = adminUserId,
                PaidAt = paidAt,
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);

            var newPaid = alreadyPaid + dto.Amount;
            if (newPaid >= installment.Amount)
            {
                installment.Status = InstallmentStatus.Paid;
                installment.PaidAt = payment.PaidAt;
            }
            else
            {
                installment.Status = InstallmentStatus.PartiallyPaid;
                installment.PaidAt = null;
            }

            booking.UpdatedAt = DateTime.UtcNow;

            try
            {
                await SaveWithUniqueReceiptNumberAsync(payment);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "This booking was updated by another payment. No payment was recorded; reload and try again.");
            }

            // The payment is committed; the receipt notification is raised afterwards and its
            // failure is absorbed here. Anything missed is picked up by the platform's
            // reconciliation sweep.
            if (_notifications != null)
            {
                try
                {
                    await _notifications.NotifyPaymentRecordedAsync(payment.Id);
                }
                catch (Exception)
                {
                    // Intentionally ignored.
                }
            }

            return await GetScheduleAsync(bookingId);
        }

        // Receipt numbers are read-max-then-insert; two concurrent payments can pick the
        // same number and the unique index rejects the loser with a raw 500. Retry the
        // save with a freshly generated number instead.
        private async Task SaveWithUniqueReceiptNumberAsync(Payment payment)
        {
            const int maxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                payment.ReceiptNumber = await GenerateReceiptNumberAsync();
                try
                {
                    await _context.SaveChangesAsync();
                    return;
                }
                catch (DbUpdateException ex) when (attempt < maxAttempts && IsReceiptNumberCollision(ex))
                {
                    // Another payment claimed this number between read and insert; retry.
                }
            }
        }

        private static bool IsReceiptNumberCollision(DbUpdateException ex)
        {
            return ex.InnerException is SqlException { Number: 2601 or 2627 } sql
                   && sql.Message.Contains("ReceiptNumber", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<int, decimal>> GetPaidByInstallmentAsync(int bookingId)
        {
            var paid = await _context.Payments
                .AsNoTracking()
                .Where(p => p.BookingId == bookingId
                            && p.InstallmentId != null
                            && p.Type == PaymentType.Installment)
                .GroupBy(p => p.InstallmentId!.Value)
                .Select(g => new { InstallmentId = g.Key, Paid = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.InstallmentId, x => x.Paid);
            // A credit aimed at an installment, and a booking-level credit BookingCreditPolicy has
            // already decided which installments it comes off, both settle the row exactly as cash
            // does. Leaving them out is what used to leave the schedule demanding money the customer
            // no longer owed. Read through the policy so the booking projections see the same figure.
            var credits = await BookingCreditPolicy.InstallmentCreditsByBookingAsync(_context, [bookingId]);
            foreach (var (installmentId, amount) in credits)
                paid[installmentId] = paid.GetValueOrDefault(installmentId) + amount;
            return paid;
        }

        private Task<decimal> ValidInstallmentCreditsAsync(int installmentId) =>
            BookingCreditPolicy.InstallmentCreditsAsync(_context, installmentId);

        // Globally unique sequential receipt number, e.g. RCP-000001.
        private async Task<string> GenerateReceiptNumberAsync()
        {
            // Fetch only the highest existing receipt number from the database instead of
            // materialising every payment row. Numbers are zero-padded ("RCP-000001"), so the
            // longest value wins, and among equal lengths the lexicographically greatest is the
            // numeric maximum.
            var latest = await _context.Payments
                .Where(p => p.ReceiptNumber != null && p.ReceiptNumber.StartsWith("RCP-"))
                .Select(p => p.ReceiptNumber!)
                .OrderByDescending(r => r.Length)
                .ThenByDescending(r => r)
                .FirstOrDefaultAsync();

            var max = 0;
            if (latest != null && int.TryParse(latest.Substring(4), out var n))
                max = n;

            return $"RCP-{(max + 1):D6}";
        }

        public async Task<InstallmentScheduleDto> GenerateScheduleAsync(int bookingId, GenerateInstallmentPlanDto dto, int adminUserId)
        {
            ValidatePlanInput(dto);

            var booking = await _context.Bookings
                .Include(b => b.Installments)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                throw new InvalidOperationException("Booking not found.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Cannot generate installments for a cancelled booking.");

            // Possession is included deliberately. Once the sale is recognised the unpaid balance
            // is an Accounts Receivable, and an installment is the only way DAMS collects one — so
            // refusing to build a schedule after possession would strand that receivable with no
            // route to payment at all. Regeneration stays gated by CanRegenerateAsync as before.
            if (booking.Status is not (BookingStatus.PaymentPlanActive or BookingStatus.PossessionGiven))
                throw new InvalidOperationException("Installment schedule can only be generated while the payment plan is active or after possession.");

            var nonCashCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, bookingId);
            var effectiveRequired = BookingCreditPolicy.EffectiveBookingAmountRequired(booking, nonCashCredits);
            if (booking.BookingAmountReceived < effectiveRequired)
                throw new InvalidOperationException("Booking amount must be fully received or credited before generating installments.");

            var hasExisting = booking.Installments.Count > 0;
            if (hasExisting)
            {
                if (!dto.Regenerate)
                    throw new InvalidOperationException("An installment schedule already exists. Set regenerate to true to replace it.");

                if (!await CanRegenerateAsync(bookingId))
                    throw new InvalidOperationException(
                        "Schedule cannot be regenerated because installment payments exist or installments are no longer all pending.");
            }

            var possessionAmount = dto.PossessionAmount;
            if (possessionAmount > 0m && !dto.PossessionDueDate.HasValue)
                throw new InvalidOperationException("Possession due date is required when a possession amount is set.");

            // Discount is a percentage of the agreed sale price; the customer only owes the net price.
            var discountAmount = Math.Round(dto.AgreedSalePrice * dto.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var netSalePrice = dto.AgreedSalePrice - discountAmount;

            // A schedule may be built after possession — that is the only route DAMS has for
            // collecting a recognised receivable — but it must not RESTATE the sale it is collecting.
            // Once BookingSaleRecognition exists the commercial terms are history: the gross price,
            // the discount and the reason for it are what the parties agreed on the day possession
            // was handed over, and the formal statements are already built on them.
            // <para>
            // Holding the NET value alone is not enough, which is what this guard used to do. A
            // different gross-and-discount split that lands on the same net leaves revenue and
            // Accounts Receivable untouched but still moves AgreedSalePrice — and that is a
            // commission basis in its own right (FinancialCalculationBasis.AgreedSalePrice), so a
            // 100,000 sale rewritten as 125,000 less 20% pays commission on 125,000. It also leaves
            // the booking no longer saying what was actually agreed.
            // </para>
            var recognisedNetSale = await _context.BookingSaleRecognitions.AsNoTracking()
                .Where(r => r.BookingId == bookingId)
                .Select(r => (decimal?)r.NetSaleValue)
                .FirstOrDefaultAsync();
            var isRecognised = recognisedNetSale.HasValue;
            if (isRecognised
                && (dto.AgreedSalePrice != booking.AgreedSalePrice || discountAmount != booking.DiscountAmount))
                throw new InvalidOperationException(
                    $"This sale was recognised at possession on agreed terms of {booking.AgreedSalePrice:0.00} "
                    + $"less {booking.DiscountAmount:0.00} discount — a net {recognisedNetSale!.Value:0.00} — and "
                    + "those terms cannot be rewritten afterwards. Generate the schedule with the same agreed "
                    + "sale price and discount; the installment dates and amounts are still yours to change.");

            if (nonCashCredits > netSalePrice - booking.BookingAmountReceived)
                throw new InvalidOperationException("Existing rebate credits exceed the revised booking balance. Reverse or adjust them before changing the plan terms.");
            var installmentPool = netSalePrice - booking.BookingAmountReceived - nonCashCredits - possessionAmount;
            if (installmentPool <= 0m)
                throw new InvalidOperationException("Installment pool must be greater than zero after discount, booking amount, rebate credits and possession amount.");

            if (hasExisting)
            {
                // Allocations point at the installments about to be deleted. They are derived
                // bookkeeping, not the credit itself — the money stays on its disbursement row —
                // so they are discarded here and rebuilt against the new schedule below.
                var replacedIds = booking.Installments.Select(i => i.Id).ToList();
                _context.RebateCreditAllocations.RemoveRange(
                    await _context.RebateCreditAllocations
                        .Where(a => replacedIds.Contains(a.InstallmentId))
                        .ToListAsync());
                _context.Installments.RemoveRange(booking.Installments);
                booking.Installments.Clear();
            }

            // Left alone once recognised — including the reason, which the schedule form does not
            // send back, so writing it would erase the discount's justification on every
            // regeneration. The two figures are equal by the guard above; not assigning them is what
            // keeps the other two commercial fields from being silently overwritten with nothing.
            if (!isRecognised)
            {
                booking.AgreedSalePrice = dto.AgreedSalePrice;
                booking.DiscountPercent = dto.DiscountPercent;
                booking.DiscountAmount = discountAmount;
                booking.DiscountReason = string.IsNullOrWhiteSpace(dto.DiscountReason) ? null : dto.DiscountReason.Trim();
            }
            booking.InstallmentFrequency = dto.Frequency;
            booking.NumberOfInstallments = dto.NumberOfInstallments;
            booking.InstallmentPlanStartDate = dto.InstallmentStartDate.Date;
            booking.PossessionAmount = possessionAmount;
            booking.PossessionDueDate = possessionAmount > 0m ? dto.PossessionDueDate?.Date : null;
            booking.InstallmentPlanGeneratedAt = DateTime.UtcNow;
            booking.InstallmentPlanGeneratedByUserId = adminUserId;
            booking.UpdatedAt = DateTime.UtcNow;

            var installments = BuildInstallmentRows(booking.Id, dto, installmentPool);
            _context.Installments.AddRange(installments);

            booking.TotalInstallmentAmount = installments.Sum(i => i.Amount);

            await _context.SaveChangesAsync();

            // The new rows now have ids, so the booking's credits can be placed on them. The pool
            // above already excludes those credits, which is why this lands only on a schedule the
            // credit could not be taken out of up front — a plan built before the rebate arrived, or
            // one whose possession installment leaves the pool short.
            await BookingCreditPolicy.ReallocateAsync(_context, bookingId, PakistanTime.Now);
            await _context.SaveChangesAsync();

            // Reload for mapping with ids assigned.
            await _context.Entry(booking).Collection(b => b.Installments).LoadAsync();

            // Freshly generated for the current balance, so nothing is left outside it by construction.
            return MapSchedule(booking, canRegenerate: true,
                await GetPaidByInstallmentAsync(bookingId), nonCashCredits, unscheduledBalance: 0m);
        }

        private static void ValidatePlanInput(GenerateInstallmentPlanDto dto)
        {
            if (dto.AgreedSalePrice <= 0m)
                throw new InvalidOperationException("Agreed sale price must be greater than zero.");

            if (dto.NumberOfInstallments < 1)
                throw new InvalidOperationException("Number of installments must be at least 1.");

            if (dto.DiscountPercent < 0m || dto.DiscountPercent > 100m)
                throw new InvalidOperationException("Discount percent must be between 0 and 100.");

            if (dto.PossessionAmount < 0m)
                throw new InvalidOperationException("Possession amount cannot be negative.");
        }

        private static List<Installment> BuildInstallmentRows(int bookingId, GenerateInstallmentPlanDto dto, decimal installmentPool)
        {
            var rows = new List<Installment>();

            if (dto.PossessionAmount > 0m)
            {
                rows.Add(new Installment
                {
                    BookingId = bookingId,
                    SequenceNumber = 0,
                    Type = InstallmentType.Possession,
                    DueDate = dto.PossessionDueDate!.Value.Date,
                    Amount = dto.PossessionAmount,
                    Status = InstallmentStatus.Pending,
                    Notes = "Possession payment"
                });
            }

            // Round DOWN so the remainder folded into the last installment is always
            // positive — rounding up could overshoot and leave the last row at zero or
            // negative, which can never be paid (payments must be > 0).
            var perInstallment = Math.Floor(installmentPool / dto.NumberOfInstallments * 100m) / 100m;
            if (perInstallment <= 0m)
                throw new InvalidOperationException(
                    "Installment pool is too small for this number of installments. Reduce the number of installments.");

            var allocated = 0m;

            for (var i = 1; i <= dto.NumberOfInstallments; i++)
            {
                var amount = i == dto.NumberOfInstallments
                    ? installmentPool - allocated
                    : perInstallment;

                allocated += amount;

                rows.Add(new Installment
                {
                    BookingId = bookingId,
                    SequenceNumber = i,
                    Type = InstallmentType.Regular,
                    DueDate = CalculateDueDate(dto.InstallmentStartDate, dto.Frequency, i),
                    Amount = amount,
                    Status = InstallmentStatus.Pending
                });
            }

            return rows;
        }

        private static DateTime CalculateDueDate(DateTime startDate, InstallmentFrequency frequency, int sequenceNumber)
        {
            var months = frequency switch
            {
                InstallmentFrequency.Monthly => sequenceNumber,
                InstallmentFrequency.Quarterly => sequenceNumber * 3,
                InstallmentFrequency.HalfYearly => sequenceNumber * 6,
                InstallmentFrequency.Yearly => sequenceNumber * 12,
                _ => sequenceNumber
            };

            return startDate.Date.AddMonths(months);
        }

        // One implementation, in BookingCreditPolicy: the rebate service asks the same question
        // before it reverses a credit the plan was built smaller by, and two answers to "can this
        // plan be rebuilt" would let one path allow what the other refuses.
        private Task<bool> CanRegenerateAsync(int bookingId) =>
            BookingCreditPolicy.CanRegenerateScheduleAsync(_context, bookingId);

        /// <summary>
        /// Refuses a receipt while the schedule demands LESS than the customer still owes.
        /// <para>
        /// Reversing a credit that a later-generated plan was built smaller by restores principal
        /// with no installment to sit on. That is repairable — regenerate — right up until the next
        /// receipt, which pins the plan and makes the shortfall permanently uncollectable: the sale
        /// can never be completed and the schedule shows nothing owing. So the reversal is allowed
        /// while the plan is still rebuildable, and the COLLECTION is what waits for the repair.
        /// </para>
        /// <para>
        /// Only the part a REVERSED credit is responsible for
        /// (<see cref="BookingCreditPolicy.UncollectableFromReversedCreditsAsync"/>) blocks a
        /// receipt. A plan that is short for some other reason — imported, or built by hand against
        /// part of the balance — is left alone: collection on it worked before this change and must
        /// keep working.
        /// </para>
        /// </summary>
        private async Task EnsureScheduleCoversTheBalanceAsync(Booking booking, decimal rebateCredits)
        {
            var scheduleTotal = await _context.Installments.AsNoTracking()
                .Where(i => i.BookingId == booking.Id)
                .SumAsync(i => (decimal?)i.Amount) ?? 0m;
            var stranded = await BookingCreditPolicy.UncollectableFromReversedCreditsAsync(
                _context, booking, scheduleTotal, rebateCredits);
            if (stranded <= 0m) return;

            throw new InvalidOperationException(
                $"A reversed rebate credit put {stranded:0.00} back on this booking that the installment "
                + "plan does not collect, so taking this payment would lock the plan with that amount "
                + "uncollectable. Regenerate the installment plan for the current balance first.");
        }

        private InstallmentScheduleDto MapSchedule(Booking booking, bool canRegenerate,
            Dictionary<int, decimal> paidByInstallment, decimal nonCashCredits, decimal unscheduledBalance)
        {
            var hasSchedule = booking.Installments.Count > 0;
            var effectiveRequired = BookingCreditPolicy.EffectiveBookingAmountRequired(booking, nonCashCredits);
            var canGenerate = booking.Status is BookingStatus.PaymentPlanActive or BookingStatus.PossessionGiven
                              && booking.BookingAmountReceived >= effectiveRequired
                              && (!hasSchedule || canRegenerate);

            var installmentPool = BookingCreditPolicy.RemainingInstallmentPool(booking, nonCashCredits);
            var today = PakistanTime.Today;

            var items = booking.Installments
                .OrderBy(i => i.Type == InstallmentType.Possession ? 0 : 1)
                .ThenBy(i => i.SequenceNumber)
                .Select(i =>
                {
                    var paid = paidByInstallment.TryGetValue(i.Id, out var p) ? p : 0m;
                    var remaining = i.Amount - paid;
                    var isPaid = i.Status == InstallmentStatus.Paid;
                    var isOverdue = !isPaid && i.DueDate.Date < today;

                    // Effective status is derived so partial-payment info is never lost
                    // and overdue does not need a background job.
                    var effectiveStatus = isPaid
                        ? InstallmentStatus.Paid
                        : isOverdue
                            ? InstallmentStatus.Overdue
                            : paid > 0m
                                ? InstallmentStatus.PartiallyPaid
                                : InstallmentStatus.Pending;

                    return new InstallmentScheduleItemDto
                    {
                        Id = i.Id,
                        SequenceNumber = i.SequenceNumber,
                        Type = i.Type,
                        DueDate = i.DueDate,
                        Amount = i.Amount,
                        Status = effectiveStatus,
                        AmountPaid = paid,
                        RemainingBalance = remaining,
                        IsOverdue = isOverdue,
                        PaidAt = i.PaidAt,
                        Notes = i.Notes
                    };
                })
                .ToList();

            return new InstallmentScheduleDto
            {
                BookingId = booking.Id,
                BookingReference = booking.BookingReference,
                BookingStatus = booking.Status,
                AgreedSalePrice = booking.AgreedSalePrice,
                DiscountAmount = booking.DiscountAmount,
                DiscountPercent = booking.DiscountPercent ?? 0m,
                BookingAmountReceived = booking.BookingAmountReceived,
                PossessionAmount = booking.PossessionAmount,
                InstallmentPool = Math.Max(0m, installmentPool),
                Frequency = booking.InstallmentFrequency,
                NumberOfInstallments = booking.NumberOfInstallments,
                InstallmentStartDate = booking.InstallmentPlanStartDate,
                PossessionDueDate = booking.PossessionDueDate,
                GeneratedAt = booking.InstallmentPlanGeneratedAt,
                HasSchedule = hasSchedule,
                CanGenerate = canGenerate,
                CanRegenerate = canRegenerate,
                ScheduleTotal = items.Sum(i => i.Amount),
                SchedulePaid = items.Sum(i => i.AmountPaid),
                ScheduleRemaining = items.Sum(i => i.RemainingBalance),
                // Named on the DTO so the screen can say the plan is short before an operator tries
                // a receipt the payment service will refuse — and it is the SAME number that refusal
                // uses, so the banner and the block can never disagree.
                UnscheduledBalance = hasSchedule ? unscheduledBalance : 0m,
                Items = items
            };
        }
    }
}
