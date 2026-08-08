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
            if (await _context.CustomerRebates.AnyAsync(r => r.BookingId == bookingId, cancellationToken))
                throw new InvalidOperationException("A customer rebate already exists for this booking. Adjust its approval or disbursements instead of duplicating it.");
            var reason = Required(dto.Reason, "Rebate reason", 2000);
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
                Reason = reason, Method = dto.Method, Status = CustomerRebateStatus.Draft,
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

        public async Task<BookingCommissionRebateWorkspaceDto> ChangeRebateStatusAsync(int bookingId, int rebateId,
            RebateStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var rebate = await _context.CustomerRebates.Include(r => r.Booking).Include(r => r.Disbursements)
                .ThenInclude(d => d.Reversals).Include(r => r.Evidence)
                .SingleOrDefaultAsync(r => r.Id == rebateId && r.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Rebate not found for this booking.");
            ApplyToken(rebate, dto.ConcurrencyToken, "rebate");
            var previous = rebate.Status; var reason = Limited(dto.Reason, "Reason", 2000);
            var action = FinancialWorkflowAction.RebateCreated;
            switch (dto.TargetStatus)
            {
                case CustomerRebateStatus.PendingApproval when previous == CustomerRebateStatus.Draft:
                    if (rebate.Evidence.Count == 0) throw new InvalidOperationException("Supporting evidence is required before submitting a rebate.");
                    rebate.Status = CustomerRebateStatus.PendingApproval; rebate.SubmittedAt = DateTime.UtcNow;
                    rebate.SubmittedByUserId = actor.UserId; rebate.SubmittedByName = actor.DisplayName;
                    action = FinancialWorkflowAction.RebateSubmitted; break;
                case CustomerRebateStatus.Approved when previous == CustomerRebateStatus.PendingApproval:
                    var approved = Money(dto.ApprovedAmount ?? rebate.FinalAmount);
                    if (approved <= 0m) throw new InvalidOperationException("Approved rebate must be greater than zero.");
                    if (approved > Money(rebate.Booking.AgreedSalePrice - rebate.Booking.DiscountAmount))
                        throw new InvalidOperationException("Approved rebate cannot exceed the booking's net sale price.");
                    if (approved != rebate.FinalAmount && reason == null)
                        throw new InvalidOperationException("A reason is required when the approved amount differs from the calculated amount.");
                    rebate.ApprovedAmount = approved; rebate.Status = CustomerRebateStatus.Approved;
                    rebate.DecisionAt = DateTime.UtcNow; rebate.DecisionByUserId = actor.UserId; rebate.DecisionByName = actor.DisplayName;
                    rebate.DecisionReason = reason; action = FinancialWorkflowAction.RebateApproved; break;
                case CustomerRebateStatus.Rejected when previous == CustomerRebateStatus.PendingApproval:
                    RequireReason(reason, "A rejection reason is required."); rebate.Status = CustomerRebateStatus.Rejected;
                    rebate.DecisionAt = DateTime.UtcNow; rebate.DecisionByUserId = actor.UserId; rebate.DecisionByName = actor.DisplayName;
                    rebate.DecisionReason = reason; action = FinancialWorkflowAction.RebateRejected; break;
                case CustomerRebateStatus.Draft when previous == CustomerRebateStatus.PendingApproval:
                    RequireReason(reason, "A return reason is required."); rebate.Status = CustomerRebateStatus.Draft;
                    rebate.DecisionAt = DateTime.UtcNow; rebate.DecisionByUserId = actor.UserId; rebate.DecisionByName = actor.DisplayName;
                    rebate.DecisionReason = reason; action = FinancialWorkflowAction.RebateReturned; break;
                case CustomerRebateStatus.Cancelled when previous is CustomerRebateStatus.Draft or CustomerRebateStatus.Approved
                    or CustomerRebateStatus.Rejected:
                    RequireReason(reason, "A cancellation reason is required.");
                    if (NetDisbursed(rebate) > 0m) throw new InvalidOperationException("Applied or paid rebates require reversal, not cancellation.");
                    rebate.Status = CustomerRebateStatus.Cancelled; rebate.CancellationOrReversalReason = reason;
                    action = FinancialWorkflowAction.RebateCancelled; break;
                default: throw new InvalidOperationException($"Rebate cannot move from {previous} to {dto.TargetStatus}.");
            }
            rebate.UpdatedAt = DateTime.UtcNow;
            Audit(action, actor, customerId: rebate.CustomerId, bookingId: bookingId, rebateId: rebate.Id,
                oldRebate: previous, newRebate: rebate.Status, previousAmount: rebate.FinalAmount,
                newAmount: rebate.ApprovedAmount ?? rebate.FinalAmount, reason: reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public Task<BookingCommissionRebateWorkspaceDto> RecordRebateDisbursementAsync(int bookingId, int rebateId,
            RecordRebateDisbursementDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(async () =>
            {
                ValidateMovement(dto.Amount, dto.IdempotencyKey, dto.AppliedAt);
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
                if (rebate.Status is not (CustomerRebateStatus.Approved or CustomerRebateStatus.PartiallyApplied))
                    throw new InvalidOperationException("Only approved or partially-applied rebates can be disbursed.");
                if (dto.Method != rebate.Method) throw new InvalidOperationException("Disbursement method must match the approved rebate method.");
                if (dto.FinanceAccountId.HasValue)
                    await _financeAccounts.EnsureSelectableAsync(dto.FinanceAccountId.Value, cancellationToken: cancellationToken);
                var amount = Money(dto.Amount); var outstanding = Money((rebate.ApprovedAmount ?? rebate.FinalAmount) - NetDisbursed(rebate));
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
                if (dto.InstallmentId.HasValue)
                    await RefreshInstallmentStatusAsync(dto.InstallmentId.Value, amount, appliedAt, cancellationToken);
                rebate.Status = amount == outstanding
                    ? (dto.Method == CustomerRebateMethod.CashOrBankPayment ? CustomerRebateStatus.Paid : CustomerRebateStatus.Applied)
                    : CustomerRebateStatus.PartiallyApplied;
                rebate.UpdatedAt = DateTime.UtcNow;
                var audit = Audit(dto.Method == CustomerRebateMethod.CashOrBankPayment ? FinancialWorkflowAction.RebatePaid : FinancialWorkflowAction.RebateApplied,
                    actor, customerId: rebate.CustomerId, bookingId: bookingId, rebateId: rebate.Id,
                    newRebate: rebate.Status, newAmount: amount, reason: dto.Reference);
                audit.RebateDisbursement = disbursement;
                await _context.SaveChangesAsync(cancellationToken);
                return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
            }, cancellationToken);

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
            var amount = Money(dto.Amount); var available = Money(disbursement.Amount - disbursement.Reversals.Sum(r => r.Amount));
            if (amount > available) throw new InvalidOperationException($"Reversal exceeds the disbursement balance of {available:0.00}.");
            _context.RebateDisbursementReversals.Add(new RebateDisbursementReversal
            {
                DisbursementId = disbursement.Id, Amount = amount, Reason = reason,
                IdempotencyKey = idempotencyKey, ReversedByUserId = actor.UserId,
                ReversedByName = actor.DisplayName, ReversedAt = DateTime.UtcNow
            });
            if (disbursement.InstallmentId.HasValue)
                await RefreshInstallmentStatusAsync(disbursement.InstallmentId.Value, -amount, DateTime.UtcNow, cancellationToken);
            var remaining = Money(NetDisbursed(rebate) - amount); var previous = rebate.Status;
            if (rebate.Booking.Status == BookingStatus.Cancelled)
                rebate.Status = remaining == 0m ? CustomerRebateStatus.Reversed : CustomerRebateStatus.ReversalRequired;
            else rebate.Status = remaining == 0m ? CustomerRebateStatus.Approved : CustomerRebateStatus.PartiallyApplied;
            rebate.UpdatedAt = DateTime.UtcNow;
            Audit(FinancialWorkflowAction.RebateDisbursementReversed, actor, customerId: rebate.CustomerId,
                bookingId: bookingId, rebateId: rebate.Id, rebateDisbursementId: disbursement.Id,
                oldRebate: previous, newRebate: rebate.Status, newAmount: amount, reason: dto.Reason);
            if (rebate.Status == CustomerRebateStatus.Reversed)
                Audit(FinancialWorkflowAction.RebateReversed, actor, customerId: rebate.CustomerId, bookingId: bookingId,
                    rebateId: rebate.Id, oldRebate: previous, newRebate: rebate.Status, reason: dto.Reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }, cancellationToken);

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

        private async Task<decimal> ValidInstallmentCreditsAsync(int installmentId, CancellationToken cancellationToken) => Money(
            (await _context.RebateDisbursements.Where(d => d.InstallmentId == installmentId)
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
            - (await _context.RebateDisbursementReversals.Where(r => r.Disbursement.InstallmentId == installmentId)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m));

        private async Task<decimal> ValidBookingRebateCreditsAsync(int bookingId, CancellationToken cancellationToken) => Money(
            (await _context.RebateDisbursements.Where(d => d.Rebate.BookingId == bookingId
                    && (d.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || d.Method == CustomerRebateMethod.InstallmentAdjustment
                        || d.Method == CustomerRebateMethod.CreditNote))
                .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m)
            - (await _context.RebateDisbursementReversals.Where(r => r.Disbursement.Rebate.BookingId == bookingId
                    && (r.Disbursement.Method == CustomerRebateMethod.OutstandingBalanceReduction
                        || r.Disbursement.Method == CustomerRebateMethod.InstallmentAdjustment
                        || r.Disbursement.Method == CustomerRebateMethod.CreditNote))
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m));

        private static decimal NetDisbursed(CustomerRebate rebate) =>
            Money(rebate.Disbursements.Sum(d => d.Amount - d.Reversals.Sum(r => r.Amount)));

        private async Task RefreshInstallmentStatusAsync(int installmentId, decimal pendingCreditChange,
            DateTime effectiveAt, CancellationToken cancellationToken)
        {
            var installment = await _context.Installments.SingleAsync(i => i.Id == installmentId, cancellationToken);
            var payments = await _context.Payments.Where(p => p.InstallmentId == installmentId && p.Type == PaymentType.Installment)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            var credits = await ValidInstallmentCreditsAsync(installmentId, cancellationToken) + pendingCreditChange;
            var covered = Money(payments + credits);
            installment.Status = covered >= installment.Amount ? InstallmentStatus.Paid
                : covered > 0m ? InstallmentStatus.PartiallyPaid : InstallmentStatus.Pending;
            installment.PaidAt = installment.Status == InstallmentStatus.Paid ? effectiveAt : null;
        }
    }
}
