using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService
    {
        public async Task<PagedResult<CustomerRebateDto>> GetRebatesAsync(CustomerRebateStatus? status, int? projectId,
            int skip, int take, CancellationToken cancellationToken = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);
            var query = _context.CustomerRebates.AsNoTracking().Include(r => r.Booking).ThenInclude(b => b.Unit)
                .Include(r => r.Customer).Include(r => r.Disbursements).ThenInclude(d => d.FinanceAccount)
                .Include(r => r.Disbursements).ThenInclude(d => d.Reversals)
                .Include(r => r.Disbursements).ThenInclude(d => d.Evidence)
                .Include(r => r.Evidence).AsSplitQuery().AsQueryable();
            if (status.HasValue) query = query.Where(r => r.Status == status.Value);
            if (projectId.HasValue) query = query.Where(r => r.Booking.Unit.ProjectId == projectId.Value);
            var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<CustomerRebateDto>
            {
                Items = rows.Take(take).Select(r => MapRebate(r)).ToList(),
                HasMore = rows.Count > take
            };
        }

        public async Task<BookingCommissionRebateWorkspaceDto> CreateRebateAsync(int bookingId, CreateCustomerRebateDto dto,
            FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            if (!Enum.IsDefined(dto.CalculationType) || !Enum.IsDefined(dto.CalculationBasis))
                throw new InvalidOperationException("Select a valid rebate calculation type and basis.");
            // Uniqueness applies only to the live rebate: a cancelled or reversed one is a closed
            // historical record and must not block a corrected replacement. This is what lets a wrong
            // rebate be superseded (cancel it, then create a new one) instead of leaving the booking
            // permanently unable to hold a rebate.
            if (await _context.CustomerRebates.AnyAsync(r => r.BookingId == bookingId
                    && r.Status != CustomerRebateStatus.Cancelled
                    && r.Status != CustomerRebateStatus.Reversed, cancellationToken))
                throw new InvalidOperationException("This booking already has a rebate. Edit it, or cancel it and add a replacement.");
            if (!Enum.IsDefined(dto.Method)) throw new InvalidOperationException("Select a valid rebate method.");
            var basis = BasisAmount(booking, dto.CalculationBasis, dto.ManualBasisAmount);
            decimal calculated;
            if (dto.CalculationType == FinancialCalculationType.Percentage)
            {
                if (dto.PercentageRate is not (> 0m and <= 100m) || dto.FixedAmount.HasValue)
                    throw new InvalidOperationException("Percentage rebate requires a rate between 0 and 100 and no fixed amount.");
                calculated = Calculate(dto.CalculationType, basis, Rate(dto.PercentageRate.Value), null);
            }
            else
            {
                if (dto.FixedAmount is not > 0m || dto.PercentageRate.HasValue)
                    throw new InvalidOperationException("Fixed rebate requires a positive fixed amount and no percentage rate.");
                calculated = Calculate(dto.CalculationType, basis, null, dto.FixedAmount);
            }
            // The typed reason is optional: the entry itself ("5% of sale price") is the record when
            // the user does not add one. The column stays non-nullable so every rebate reads back with
            // something meaningful in the log and on the approval screen.
            var reason = Limited(dto.Reason, "Rebate reason", 2000)
                ?? DescribeCalculation(dto.CalculationType, dto.PercentageRate, dto.FixedAmount, dto.CalculationBasis);
            var adjustment = Money(dto.AdjustmentAmount); var adjustmentReason = Limited(dto.AdjustmentReason, "Adjustment reason", 2000);
            if (adjustment != 0m && adjustmentReason == null) throw new InvalidOperationException("An adjustment reason is required.");
            var final = Money(calculated + adjustment);
            if (final <= 0m) throw new InvalidOperationException("Final rebate must be greater than zero.");
            var netPrice = Money(booking.AgreedSalePrice - booking.DiscountAmount);
            if (final > netPrice) throw new InvalidOperationException("Rebate cannot exceed the booking's net sale price.");
            var rebate = new CustomerRebate
            {
                BookingId = bookingId, CustomerId = booking.CustomerId, CalculationType = dto.CalculationType,
                PercentageRate = dto.PercentageRate.HasValue ? Rate(dto.PercentageRate.Value) : null,
                FixedAmount = dto.FixedAmount.HasValue ? Money(dto.FixedAmount.Value) : null,
                CalculationBasis = dto.CalculationBasis, BasisAmount = basis, CalculatedAmount = calculated,
                AdjustmentAmount = adjustment, AdjustmentReason = adjustmentReason, FinalAmount = final,
                // Agreeing the rebate is the decision, so it starts pending: ready to be applied or
                // paid, and it completes itself once the disbursements cover it.
                Reason = reason, Method = dto.Method, Status = CustomerRebateStatus.Pending,
                Notes = Limited(dto.Notes, "Notes", 2000), CreatedByUserId = actor.UserId,
                CreatedByName = actor.DisplayName, CreatedAt = DateTime.UtcNow
            };
            _context.CustomerRebates.Add(rebate);
            var audit = Audit(FinancialWorkflowAction.RebateCreated, actor, customerId: booking.CustomerId, bookingId: bookingId,
                newRebate: rebate.Status, newAmount: rebate.FinalAmount, reason: reason);
            audit.Rebate = rebate;
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public async Task<BookingCommissionRebateWorkspaceDto> UpdateRebateAsync(int bookingId, int rebateId,
            UpdateCustomerRebateDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var rebate = await _context.CustomerRebates.Include(r => r.Disbursements).ThenInclude(d => d.Reversals)
                .SingleOrDefaultAsync(r => r.Id == rebateId && r.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Rebate not found for this booking.");
            ApplyToken(rebate, dto.ConcurrencyToken, "rebate");
            // Same rule as a commission: correctable while it is still only an agreement, and
            // unwound by reversing the disbursement once any of it has actually reached the customer.
            if (rebate.Status != CustomerRebateStatus.Pending)
                throw new InvalidOperationException("Only a pending rebate can be edited.");
            if (NetDisbursed(rebate) != 0m || rebate.Disbursements.Count != 0)
                throw new InvalidOperationException("A rebate with application or payment history cannot be edited.");

            var changeReason = Required(dto.ChangeReason, "Change reason", 2000);
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            if (!Enum.IsDefined(dto.CalculationType) || !Enum.IsDefined(dto.CalculationBasis))
                throw new InvalidOperationException("Select a valid rebate calculation type and basis.");
            if (!Enum.IsDefined(dto.Method)) throw new InvalidOperationException("Select a valid rebate method.");
            var reason = Limited(dto.Reason, "Rebate reason", 2000)
                ?? DescribeCalculation(dto.CalculationType, dto.PercentageRate, dto.FixedAmount, dto.CalculationBasis);
            var basis = BasisAmount(booking, dto.CalculationBasis, dto.ManualBasisAmount);
            decimal calculated;
            if (dto.CalculationType == FinancialCalculationType.Percentage)
            {
                if (dto.PercentageRate is not (> 0m and <= 100m) || dto.FixedAmount.HasValue)
                    throw new InvalidOperationException("Percentage rebate requires a rate between 0 and 100 and no fixed amount.");
                calculated = Calculate(dto.CalculationType, basis, Rate(dto.PercentageRate.Value), null);
            }
            else
            {
                if (dto.FixedAmount is not > 0m || dto.PercentageRate.HasValue)
                    throw new InvalidOperationException("Fixed rebate requires a positive fixed amount and no percentage rate.");
                calculated = Calculate(dto.CalculationType, basis, null, dto.FixedAmount);
            }
            var adjustment = Money(dto.AdjustmentAmount);
            var adjustmentReason = Limited(dto.AdjustmentReason, "Adjustment reason", 2000);
            if (adjustment != 0m && adjustmentReason == null)
                throw new InvalidOperationException("An adjustment reason is required.");
            var final = Money(calculated + adjustment);
            if (final <= 0m) throw new InvalidOperationException("Final rebate must be greater than zero.");
            if (final > Money(booking.AgreedSalePrice - booking.DiscountAmount))
                throw new InvalidOperationException("Rebate cannot exceed the booking's net sale price.");

            var previousAmount = rebate.FinalAmount;
            rebate.CalculationType = dto.CalculationType;
            rebate.PercentageRate = dto.PercentageRate.HasValue ? Rate(dto.PercentageRate.Value) : null;
            rebate.FixedAmount = dto.FixedAmount.HasValue ? Money(dto.FixedAmount.Value) : null;
            rebate.CalculationBasis = dto.CalculationBasis;
            rebate.BasisAmount = basis;
            rebate.CalculatedAmount = calculated;
            rebate.AdjustmentAmount = adjustment;
            rebate.AdjustmentReason = adjustmentReason;
            rebate.FinalAmount = final;
            rebate.Reason = reason;
            rebate.Method = dto.Method;
            rebate.Notes = Limited(dto.Notes, "Notes", 2000);
            // A correction leaves the rebate pending, for the corrected amount.
            rebate.UpdatedAt = DateTime.UtcNow;
            Audit(FinancialWorkflowAction.RebateAdjusted, actor, customerId: booking.CustomerId, bookingId: bookingId,
                rebateId: rebate.Id, oldRebate: rebate.Status, newRebate: rebate.Status,
                previousAmount: previousAmount, newAmount: rebate.FinalAmount, reason: changeReason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public async Task<BookingCommissionRebateWorkspaceDto> ChangeRebateStatusAsync(int bookingId, int rebateId,
            RebateStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var rebate = await _context.CustomerRebates.Include(r => r.Booking).Include(r => r.Disbursements)
                .ThenInclude(d => d.Reversals).Include(r => r.Evidence)
                .SingleOrDefaultAsync(r => r.Id == rebateId && r.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Rebate not found for this booking.");
            ApplyToken(rebate, dto.ConcurrencyToken, "rebate");
            var previous = rebate.Status; var reason = Limited(dto.Reason, "Reason", 2000);
            FinancialWorkflowAction action;
            switch (dto.TargetStatus)
            {
                // As with a commission, cancelling is the only status left for a person to choose:
                // the rebate is pending from entry and completes itself once it has all been applied
                // or paid. Anything already given to the customer is unwound by reversing it.
                case CustomerRebateStatus.Cancelled when previous == CustomerRebateStatus.Pending:
                    RequireReason(reason, "A cancellation reason is required.");
                    if (NetDisbursed(rebate) > 0m) throw new InvalidOperationException("Applied or paid rebates require reversal, not cancellation.");
                    rebate.Status = CustomerRebateStatus.Cancelled; rebate.CancellationOrReversalReason = reason;
                    action = FinancialWorkflowAction.RebateCancelled; break;
                default: throw new InvalidOperationException($"Rebate cannot move from {previous} to {dto.TargetStatus}.");
            }
            rebate.UpdatedAt = DateTime.UtcNow;
            Audit(action, actor, customerId: rebate.CustomerId, bookingId: bookingId, rebateId: rebate.Id,
                oldRebate: previous, newRebate: rebate.Status, previousAmount: rebate.FinalAmount,
                newAmount: rebate.FinalAmount, reason: reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public Task<BookingCommissionRebateWorkspaceDto> RecordRebateDisbursementAsync(int bookingId, int rebateId,
            RecordRebateDisbursementDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(async () =>
            {
                await ValidateMovementAsync(dto.Amount, dto.IdempotencyKey, dto.AppliedAt, cancellationToken);
                ValidateRebateMethod(dto);
                var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
                var appliedAt = dto.AppliedAt.Date;
                var reference = Limited(dto.Reference, "Reference", 200);
                var notes = Limited(dto.Notes, "Notes", 2000);
                if ((dto.Method == CustomerRebateMethod.CreditNote
                        || (dto.Method == CustomerRebateMethod.CashOrBankPayment && dto.PaymentMethod != PaymentMethod.Cash))
                    && reference == null)
                    throw new InvalidOperationException("A reference is required for this rebate method.");
                var existing = await _context.RebateDisbursements.AsNoTracking().Include(d => d.Rebate)
                    .SingleOrDefaultAsync(d => d.IdempotencyKey == idempotencyKey, cancellationToken);
                if (existing != null)
                {
                    if (existing.RebateId != rebateId || existing.Amount != Money(dto.Amount) || existing.Method != dto.Method
                        || existing.Rebate.BookingId != bookingId || existing.AppliedAt != appliedAt
                        || existing.FinanceAccountId != dto.FinanceAccountId || existing.InstallmentId != dto.InstallmentId
                        || existing.PaymentMethod != dto.PaymentMethod || existing.Reference != reference || existing.Notes != notes)
                        throw new InvalidOperationException("This idempotency key was already used for a different rebate disbursement.");
                    return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
                }
                // See RecordPayoutAsync: a fully reversed payout/disbursement no longer holds a live
                // balance, so its reference is reusable; partially reversed rows keep it blocked.
                if (reference != null && (dto.FinanceAccountId.HasValue
                        ? await _context.CommissionPayouts.AnyAsync(p => p.FinanceAccountId == dto.FinanceAccountId
                                && p.PaymentReference == reference
                                && p.Amount > p.Reversals.Sum(r => r.Amount), cancellationToken)
                            || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == dto.FinanceAccountId
                                && d.Reference == reference
                                && d.Amount > d.Reversals.Sum(r => r.Amount), cancellationToken)
                            || await _context.BookingCancellationRefunds.AnyAsync(r => r.FinanceAccountId == dto.FinanceAccountId
                                && r.PaymentReference == reference, cancellationToken)
                        : await _context.RebateDisbursements.AnyAsync(d => d.RebateId == rebateId
                            && d.Reference == reference
                            && d.Amount > d.Reversals.Sum(r => r.Amount), cancellationToken)))
                    throw new InvalidOperationException("This reference is already recorded for the selected account or rebate.");
                var rebate = await _context.CustomerRebates.Include(r => r.Booking).ThenInclude(b => b.Payments)
                    .Include(r => r.Booking).ThenInclude(b => b.Installments)
                    .Include(r => r.Disbursements).ThenInclude(d => d.Reversals)
                    .SingleOrDefaultAsync(r => r.Id == rebateId && r.BookingId == bookingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Rebate not found for this booking.");
                ApplyToken(rebate, dto.RebateConcurrencyToken, "rebate"); EnsureActiveBooking(rebate.Booking);
                if (rebate.Status != CustomerRebateStatus.Pending)
                    throw new InvalidOperationException("Only a pending rebate can be applied or paid.");
                // How a rebate is delivered is decided when it is first applied, not when it is entered,
                // so the entry screen stays a plain amount. The first disbursement locks the method:
                // every disbursement of one rebate still shares a single method, which is what the
                // non-cash credit totals and the Applied-vs-Paid end state both depend on.
                if (rebate.Disbursements.Count == 0) rebate.Method = dto.Method;
                else if (dto.Method != rebate.Method)
                    throw new InvalidOperationException($"This rebate is already being applied as {MethodLabel(rebate.Method)}. Every payment or credit for one rebate must use the same method.");
                if (dto.FinanceAccountId.HasValue)
                    await _financeAccounts.EnsureSelectableAsync(dto.FinanceAccountId.Value, cancellationToken: cancellationToken);
                var amount = Money(dto.Amount); var outstanding = Money(rebate.FinalAmount - NetDisbursed(rebate));
                if (amount > outstanding) throw new InvalidOperationException($"Disbursement exceeds the outstanding rebate of {outstanding:0.00}.");
                if (dto.Method == CustomerRebateMethod.InstallmentAdjustment)
                {
                    var installment = rebate.Booking.Installments.SingleOrDefault(i => i.Id == dto.InstallmentId)
                        ?? throw new InvalidOperationException("Select an installment belonging to this booking.");
                    var paid = rebate.Booking.Payments.Where(p => p.InstallmentId == installment.Id).Sum(p => p.Amount);
                    var credits = await ValidInstallmentCreditsAsync(installment.Id, cancellationToken);
                    var remaining = Money(installment.Amount - paid - credits);
                    if (amount > remaining) throw new InvalidOperationException($"Adjustment exceeds the installment balance of {remaining:0.00}.");
                }
                else if (dto.Method is CustomerRebateMethod.OutstandingBalanceReduction or CustomerRebateMethod.CreditNote)
                {
                    var collected = rebate.Booking.Payments.Sum(p => p.Amount);
                    var credits = await ValidBookingRebateCreditsAsync(bookingId, cancellationToken);
                    var remaining = Money(rebate.Booking.AgreedSalePrice - rebate.Booking.DiscountAmount - collected - credits);
                    if (amount > remaining) throw new InvalidOperationException($"Reduction exceeds the booking balance of {remaining:0.00}.");
                }
                var disbursement = new RebateDisbursement
                {
                    RebateId = rebate.Id, FinanceAccountId = dto.FinanceAccountId, InstallmentId = dto.InstallmentId,
                    Method = dto.Method, Amount = amount, AppliedAt = appliedAt, PaymentMethod = dto.PaymentMethod,
                    Reference = reference, IdempotencyKey = idempotencyKey,
                    Notes = notes, RecordedByUserId = actor.UserId,
                    RecordedByName = actor.DisplayName, RecordedAt = DateTime.UtcNow
                };
                _context.RebateDisbursements.Add(disbursement);
                // Part of it going out leaves the rebate Pending; how much has been given and how
                // much is left is read from the disbursement rows.
                rebate.Status = amount == outstanding
                    ? (dto.Method == CustomerRebateMethod.CashOrBankPayment ? CustomerRebateStatus.Paid : CustomerRebateStatus.Applied)
                    : CustomerRebateStatus.Pending;
                rebate.UpdatedAt = DateTime.UtcNow;
                var audit = Audit(dto.Method == CustomerRebateMethod.CashOrBankPayment ? FinancialWorkflowAction.RebatePaid : FinancialWorkflowAction.RebateApplied,
                    actor, customerId: rebate.CustomerId, bookingId: bookingId, rebateId: rebate.Id,
                    newRebate: rebate.Status, newAmount: amount, reason: dto.Reference);
                audit.RebateDisbursement = disbursement;
                if (BookingCreditPolicy.IsNonCashCredit(dto.Method))
                    await ReconcileBookingAmountMilestoneAsync(bookingId, amount, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                // Land the credit on the installment schedule — second save, same transaction, so
                // the reallocation sees the row just written and commits with it or not at all.
                // Without this the plan keeps demanding money the customer no longer owes: the tail
                // installment can never close and the sale can never be completed.
                if (BookingCreditPolicy.IsNonCashCredit(dto.Method))
                {
                    await BookingCreditPolicy.ReallocateAsync(_context, bookingId, appliedAt, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
            }, cancellationToken);

        // A booking-level credit (balance reduction / credit note / installment adjustment) can cover
        // the booking-amount milestone entirely on its own. That milestone is normally advanced by a
        // cash booking-amount payment, but once credits meet the effective requirement there is no
        // remaining cash to collect — and therefore no payment that could ever trigger the advance —
        // so the booking would deadlock in AwaitingBookingAmount. Advance it here using the same rule
        // as BookingService.RecordBookingAmountPaymentAsync (net price minus non-cash credits, capped
        // at the required amount). Runs after the disbursement is saved so the just-applied credit is
        // visible to the DB-side credit sum.
        private async Task ReconcileBookingAmountMilestoneAsync(
            int bookingId,
            decimal pendingCreditDelta,
            CancellationToken cancellationToken)
        {
            var booking = await _context.Bookings.Include(b => b.Unit).Include(b => b.Installments)
                .Include(b => b.Payments)
                .SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
            if (booking is null || booking.BookingAmountRequired <= 0m
                || booking.Status is not (BookingStatus.AwaitingBookingAmount or BookingStatus.PaymentPlanActive))
                return;

            var persistedCredits = await BookingCreditPolicy.GetNonCashCreditsAsync(_context, bookingId, cancellationToken);
            var effectiveRequired = BookingCreditPolicy.EffectiveBookingAmountRequired(
                booking, Money(persistedCredits + pendingCreditDelta));
            var satisfied = booking.BookingAmountReceived >= effectiveRequired;
            if (booking.Status == BookingStatus.AwaitingBookingAmount && satisfied)
            {
                booking.Status = BookingStatus.PaymentPlanActive;
                booking.BookingAmountConfirmedDate ??= PakistanTime.Now;
                booking.InstallmentPlanStartDate ??= PakistanTime.Now;
                booking.UpdatedAt = DateTime.UtcNow;
                if (booking.Unit != null)
                {
                    booking.Unit.Status = UnitStatus.OnPaymentPlan;
                    booking.Unit.UpdatedAt = DateTime.UtcNow;
                }
                return;
            }

            if (booking.Status != BookingStatus.PaymentPlanActive || satisfied)
                return;

            var hasPlanActivity = booking.Installments.Count != 0
                || booking.InstallmentPlanGeneratedAt.HasValue
                || booking.Payments.Any(p => p.Type == PaymentType.Installment);
            if (hasPlanActivity)
                throw new InvalidOperationException(
                    "This reversal would invalidate the booking-amount milestone after installment activity began. Adjust or reopen the payment plan before reversing the credit.");

            booking.Status = BookingStatus.AwaitingBookingAmount;
            booking.BookingAmountConfirmedDate = null;
            booking.InstallmentPlanStartDate = null;
            booking.UpdatedAt = DateTime.UtcNow;
            if (booking.Unit?.Status == UnitStatus.OnPaymentPlan)
            {
                booking.Unit.Status = UnitStatus.Reserved;
                booking.Unit.UpdatedAt = DateTime.UtcNow;
            }
        }

        public Task<BookingCommissionRebateWorkspaceDto> ReverseRebateDisbursementAsync(int bookingId, int rebateId,
            int disbursementId, ReverseMoneyMovementDto dto, FinancialWorkflowActor actor,
            CancellationToken cancellationToken = default) => SerializableAsync(async () =>
        {
            ValidateReversal(dto);
            var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
            var reason = Required(dto.Reason, "Reversal reason", 2000);
            var existing = await _context.RebateDisbursementReversals.AsNoTracking()
                .Include(r => r.Disbursement).ThenInclude(d => d.Rebate)
                .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing != null)
            {
                if (existing.DisbursementId != disbursementId || existing.Amount != Money(dto.Amount)
                    || existing.Disbursement.RebateId != rebateId || existing.Disbursement.Rebate.BookingId != bookingId
                    || existing.Reason != reason)
                    throw new InvalidOperationException("This idempotency key was already used for a different reversal.");
                return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
            }
            var rebate = await _context.CustomerRebates.Include(r => r.Booking).Include(r => r.Disbursements)
                .ThenInclude(d => d.Reversals).SingleOrDefaultAsync(r => r.Id == rebateId && r.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Rebate not found for this booking.");
            var disbursement = rebate.Disbursements.SingleOrDefault(d => d.Id == disbursementId)
                ?? throw new KeyNotFoundException("Disbursement not found for this rebate.");
            // A booking-level credit (balance reduction / credit note / installment adjustment) can be
            // counted toward sale completion. Reversing it on an already completed or possession-given
            // booking would restore a receivable with no valid collection path and leave the unit sold,
            // so block it until the booking is reopened rather than silently stranding the balance.
            // (Cancelled bookings intentionally keep the reversal path: that is how they reach Reversed.)
            if (disbursement.Method is CustomerRebateMethod.OutstandingBalanceReduction
                    or CustomerRebateMethod.CreditNote or CustomerRebateMethod.InstallmentAdjustment
                && rebate.Booking.Status is BookingStatus.SaleCompleted or BookingStatus.PossessionGiven)
                throw new InvalidOperationException(
                    "This credit was applied to a completed or possession-given booking. Reopen the booking before reversing the credit.");
            var amount = Money(dto.Amount); var available = Money(disbursement.Amount - disbursement.Reversals.Sum(r => r.Amount));
            if (amount > available) throw new InvalidOperationException($"Reversal exceeds the disbursement balance of {available:0.00}.");
            if (BookingCreditPolicy.IsNonCashCredit(disbursement.Method))
                await EnsureRestoredDebtIsCollectableAsync(rebate.Booking, amount, cancellationToken);
            _context.RebateDisbursementReversals.Add(new RebateDisbursementReversal
            {
                DisbursementId = disbursement.Id, Amount = amount, Reason = reason,
                IdempotencyKey = idempotencyKey, ReversedByUserId = actor.UserId,
                ReversedByName = actor.DisplayName, ReversedAt = PakistanTime.Now
            });
            // NetDisbursed already reflects this reversal (EF fixup tracked it into
            // disbursement.Reversals above); subtracting `amount` again would double-count and
            // leave a fully reversed rebate stuck in PartiallyApplied / ReversalRequired.
            var remaining = NetDisbursed(rebate); var previous = rebate.Status;
            if (rebate.Booking.Status == BookingStatus.Cancelled)
                rebate.Status = remaining == 0m ? CustomerRebateStatus.Reversed : CustomerRebateStatus.ReversalRequired;
            else rebate.Status = CustomerRebateStatus.Pending;
            rebate.UpdatedAt = DateTime.UtcNow;
            Audit(FinancialWorkflowAction.RebateDisbursementReversed, actor, customerId: rebate.CustomerId,
                bookingId: bookingId, rebateId: rebate.Id, rebateDisbursementId: disbursement.Id,
                oldRebate: previous, newRebate: rebate.Status, newAmount: amount, reason: dto.Reason);
            if (rebate.Status == CustomerRebateStatus.Reversed)
                Audit(FinancialWorkflowAction.RebateReversed, actor, customerId: rebate.CustomerId, bookingId: bookingId,
                    rebateId: rebate.Id, oldRebate: previous, newRebate: rebate.Status, reason: dto.Reason);
            if (BookingCreditPolicy.IsNonCashCredit(disbursement.Method))
                await ReconcileBookingAmountMilestoneAsync(bookingId, -amount, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            // Taking the credit back puts the balance back on the plan. Rebuilding the allocation
            // from the rows — rather than unwinding it by hand — is what makes a partial reversal
            // need no special case.
            if (BookingCreditPolicy.IsNonCashCredit(disbursement.Method))
            {
                await BookingCreditPolicy.ReallocateAsync(_context, bookingId, PakistanTime.Now, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }, cancellationToken);

        /// <summary>
        /// Refuses a credit reversal that would put debt back on a booking the installment plan can
        /// no longer collect.
        /// <para>
        /// A plan generated while the credit stood was built SMALLER by it —
        /// <see cref="BookingCreditPolicy.RemainingInstallmentPool"/> takes the credit out of the
        /// pool — so the principal now being restored was never given a row to sit on. Rebuilding
        /// the allocation cannot put it back: an allocation only moves an existing installment's
        /// balance, it cannot create one. And a receipt is capped by the installment it is recorded
        /// against, so once the plan is pinned the restored amount has no collection path at all: it
        /// silently blocks completion for ever while the schedule shows nothing owing.
        /// </para>
        /// <para>
        /// Regenerating the plan is the repair, so the reversal is only refused when the plan can no
        /// longer be regenerated — the same shape as
        /// <see cref="ReconcileBookingAmountMilestoneAsync"/>, which refuses a reversal that would
        /// unwind the booking-amount milestone only once installment activity has begun. While the
        /// plan IS still rebuildable the shortfall is real but repairable, and
        /// <c>InstallmentService.RecordInstallmentPaymentAsync</c> refuses to take the next receipt
        /// until it has been repaired — otherwise that receipt would pin the plan and turn a
        /// repairable state into the dead end this guard exists to prevent.
        /// </para>
        /// <para>
        /// A CANCELLED booking is out of scope entirely. Cancelling voids the sale, so there is no
        /// sale receivable left to collect and no plan to rebuild; the settlement is what the parties
        /// still owe each other. Reversing the credit is how a cancelled booking's rebate reaches
        /// Reversed, and refusing it here would strand the record in ReversalRequired for ever. This
        /// is not a loosening of the possession/completion restriction above, which still stands.
        /// </para>
        /// </summary>
        private async Task EnsureRestoredDebtIsCollectableAsync(
            Booking booking, decimal reversalAmount, CancellationToken cancellationToken)
        {
            if (booking.Status == BookingStatus.Cancelled) return;

            var scheduleTotal = await _context.Installments.AsNoTracking()
                .Where(i => i.BookingId == booking.Id)
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;
            if (scheduleTotal <= 0m) return;

            var creditsAfterReversal = Money(
                await BookingCreditPolicy.GetNonCashCreditsAsync(_context, booking.Id, cancellationToken)
                - reversalAmount);
            // Only what a reversed credit is responsible for — a plan that was already short for some
            // other reason is a pre-existing condition this reversal did not create, and refusing on
            // its account would block a correction that has nothing to do with it.
            var shortfall = await BookingCreditPolicy.UncollectableFromReversedCreditsAsync(
                _context, booking, scheduleTotal, creditsAfterReversal, reversalAmount, cancellationToken);
            if (shortfall <= 0m) return;
            if (await BookingCreditPolicy.CanRegenerateScheduleAsync(_context, booking.Id, cancellationToken)) return;

            throw new InvalidOperationException(
                $"Reversing this credit puts {shortfall:0.00} back on the customer's balance, but the "
                + "installment plan was generated without it and can no longer be regenerated, so there "
                + "would be no way to collect it. Reverse the installment payments, or leave the credit "
                + "in place and correct it another way.");
        }

        private static void ValidateRebateMethod(RecordRebateDisbursementDto dto)
        {
            if (!Enum.IsDefined(dto.Method))
                throw new InvalidOperationException("Select a valid rebate method.");
            if (dto.Method == CustomerRebateMethod.CashOrBankPayment)
            {
                if (!dto.FinanceAccountId.HasValue || !dto.PaymentMethod.HasValue)
                    throw new InvalidOperationException("Cash or bank rebate payments require a finance account and payment method.");
                if (!Enum.IsDefined(dto.PaymentMethod.Value))
                    throw new InvalidOperationException("Select a valid rebate payment method.");
                if (dto.InstallmentId.HasValue) throw new InvalidOperationException("Cash rebates cannot target an installment.");
            }
            else
            {
                if (dto.FinanceAccountId.HasValue || dto.PaymentMethod.HasValue)
                    throw new InvalidOperationException("Only cash or bank rebate payments use a finance account and payment method.");
                if (dto.Method == CustomerRebateMethod.InstallmentAdjustment && !dto.InstallmentId.HasValue)
                    throw new InvalidOperationException("Select an installment for the adjustment.");
                if (dto.Method != CustomerRebateMethod.InstallmentAdjustment && dto.InstallmentId.HasValue)
                    throw new InvalidOperationException("The selected rebate method cannot target an installment.");
            }
            if (dto.Method == CustomerRebateMethod.Other && string.IsNullOrWhiteSpace(dto.Notes))
                throw new InvalidOperationException("Describe the other rebate method in notes.");
        }

        // Includes the share of any booking-level credit already sitting on this installment, so a
        // targeted adjustment cannot be stacked on top of a balance reduction that has already
        // settled the same money.
        private Task<decimal> ValidInstallmentCreditsAsync(int installmentId, CancellationToken cancellationToken) =>
            BookingCreditPolicy.InstallmentCreditsAsync(_context, installmentId, cancellationToken);

        private Task<decimal> ValidBookingRebateCreditsAsync(int bookingId, CancellationToken cancellationToken) =>
            BookingCreditPolicy.GetNonCashCreditsAsync(_context, bookingId, cancellationToken);

        private static decimal NetDisbursed(CustomerRebate rebate) =>
            Money(rebate.Disbursements.Sum(d => d.Amount - d.Reversals.Sum(r => r.Amount)));


    }
}
