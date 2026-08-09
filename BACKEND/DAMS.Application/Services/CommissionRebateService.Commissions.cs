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
        public async Task<PagedResult<BookingCommissionDto>> GetCommissionsAsync(BookingCommissionStatus? status,
            int? partnerId, int? projectId, int skip, int take, CancellationToken cancellationToken = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);
            var query = _context.BookingCommissions.AsNoTracking()
                .Include(c => c.Booking).ThenInclude(b => b.Unit)
                .Include(c => c.Partner).Include(c => c.RuleRevision)
                .Include(c => c.Payouts).ThenInclude(p => p.FinanceAccount)
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                .Include(c => c.Payouts).ThenInclude(p => p.Evidence)
                .Include(c => c.Evidence).AsSplitQuery().AsQueryable();
            if (status.HasValue) query = query.Where(c => c.Status == status.Value);
            if (partnerId.HasValue) query = query.Where(c => c.PartnerId == partnerId.Value);
            if (projectId.HasValue) query = query.Where(c => c.Booking.Unit.ProjectId == projectId.Value);
            var rows = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                .Skip(skip).Take(take + 1).ToListAsync(cancellationToken);
            return new PagedResult<BookingCommissionDto>
            {
                Items = rows.Take(take).Select(c => MapCommission(c)).ToList(),
                HasMore = rows.Count > take
            };
        }

        public async Task<BookingCommissionRebateWorkspaceDto> CreateCommissionAsync(int bookingId,
            CreateBookingCommissionDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            // Uniqueness applies only to the live commission for this partner: a rejected, cancelled, or
            // reversed one is a closed historical record and must not block a corrected replacement.
            if (await _context.BookingCommissions.AnyAsync(c => c.BookingId == bookingId && c.PartnerId == dto.PartnerId
                    && c.Status != BookingCommissionStatus.Rejected
                    && c.Status != BookingCommissionStatus.Cancelled
                    && c.Status != BookingCommissionStatus.Reversed, cancellationToken))
                throw new InvalidOperationException("An active commission already exists for this partner and booking. Reject or cancel it before creating a replacement.");
            var partner = await _context.ThirdPartyPartners.AsNoTracking().SingleOrDefaultAsync(p => p.Id == dto.PartnerId, cancellationToken)
                ?? throw new InvalidOperationException("Partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Inactive partners cannot receive new commissions.");
            var attribution = dto.AttributionId.HasValue
                ? await _context.ThirdPartyAttributions.SingleOrDefaultAsync(a => a.Id == dto.AttributionId && a.BookingId == bookingId, cancellationToken)
                : await _context.ThirdPartyAttributions.SingleOrDefaultAsync(a => a.BookingId == bookingId && a.PartnerId == dto.PartnerId, cancellationToken);
            if (attribution == null || attribution.PartnerId != dto.PartnerId)
                throw new InvalidOperationException("Create a booking-level attribution for this partner before calculating commission.");

            var commission = new BookingCommission
            {
                BookingId = bookingId, PartnerId = partner.Id, AttributionId = attribution.Id,
                PartnerNameSnapshot = partner.Name, PartnerTypeSnapshot = partner.PartnerType,
                PartnerInternalCodeSnapshot = partner.InternalCode,
                AllocationPercentSnapshot = attribution.AllocationPercent,
                IsManual = dto.IsManual, CreatedByUserId = actor.UserId, CreatedByName = actor.DisplayName,
                CreatedAt = DateTime.UtcNow, Status = BookingCommissionStatus.Draft
            };
            if (dto.IsManual) ApplyManualCalculation(commission, booking, dto);
            else await ApplyRuleCalculationAsync(commission, booking, partner, dto.RuleId, cancellationToken);
            ApplyCommissionAdjustment(commission, dto.AdjustmentAmount, dto.AdjustmentReason,
                dto.IsManual ? dto.ManualReason : null, booking);
            if (commission.FinalAmount <= 0m)
                throw new InvalidOperationException("Final commission must be greater than zero.");
            if (!commission.IsManual && !commission.RequiresApprovalSnapshot)
            {
                commission.Status = BookingCommissionStatus.Approved;
                commission.ApprovedAmount = commission.FinalAmount;
                commission.DecisionAt = DateTime.UtcNow;
                commission.DecisionByUserId = actor.UserId;
                commission.DecisionByName = actor.DisplayName;
                commission.DecisionReason = "Approval inherited from the applied rule.";
            }
            _context.BookingCommissions.Add(commission);
            var audit = Audit(FinancialWorkflowAction.CommissionCreated, actor, partner.Id, booking.CustomerId, bookingId,
                newCommission: commission.RequiresApprovalSnapshot ? commission.Status : BookingCommissionStatus.Draft,
                newAmount: commission.FinalAmount,
                reason: commission.IsManual ? commission.ManualReason : commission.RuleNameSnapshot);
            audit.Commission = commission;
            var calculationAudit = Audit(FinancialWorkflowAction.CommissionCalculated, actor, partner.Id,
                booking.CustomerId, bookingId, newAmount: commission.CalculatedAmount,
                reason: commission.RuleNameSnapshot ?? commission.ManualReason, commissionRuleId: commission.RuleId,
                commissionRuleRevisionId: commission.RuleRevisionId);
            calculationAudit.Commission = commission;
            if (commission.AdjustmentAmount != 0m)
            {
                var adjustmentAudit = Audit(FinancialWorkflowAction.CommissionAdjusted, actor, partner.Id,
                    booking.CustomerId, bookingId, previousAmount: commission.CalculatedAmount,
                    newAmount: commission.FinalAmount, reason: commission.AdjustmentReason,
                    commissionRuleId: commission.RuleId);
                adjustmentAudit.Commission = commission;
            }
            if (commission.Status == BookingCommissionStatus.Approved)
            {
                var approvalAudit = Audit(FinancialWorkflowAction.CommissionApproved, actor, partner.Id,
                    booking.CustomerId, bookingId, oldCommission: BookingCommissionStatus.Draft,
                    newCommission: BookingCommissionStatus.Approved,
                    newAmount: commission.ApprovedAmount, reason: commission.DecisionReason);
                approvalAudit.Commission = commission;
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public async Task<BookingCommissionRebateWorkspaceDto> UpdateCommissionAsync(int bookingId, int commissionId,
            UpdateBookingCommissionDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var commission = await _context.BookingCommissions
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Commission not found for this booking.");
            ApplyToken(commission, dto.ConcurrencyToken, "commission");
            if (commission.Status != BookingCommissionStatus.Draft)
                throw new InvalidOperationException("Only a draft commission can be edited. Return it for correction first.");
            if (NetPaid(commission) != 0m)
                throw new InvalidOperationException("A commission with payout history cannot be edited.");

            var changeReason = Required(dto.ChangeReason, "Change reason", 2000);
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            if (await _context.BookingCommissions.AnyAsync(c => c.Id != commissionId && c.BookingId == bookingId
                    && c.PartnerId == dto.PartnerId && c.Status != BookingCommissionStatus.Rejected
                    && c.Status != BookingCommissionStatus.Cancelled && c.Status != BookingCommissionStatus.Reversed,
                    cancellationToken))
                throw new InvalidOperationException("An active commission already exists for this partner and booking.");
            var partner = await _context.ThirdPartyPartners.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == dto.PartnerId, cancellationToken)
                ?? throw new InvalidOperationException("Partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Inactive partners cannot receive new commissions.");
            var attribution = dto.AttributionId.HasValue
                ? await _context.ThirdPartyAttributions.AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == dto.AttributionId && a.BookingId == bookingId, cancellationToken)
                : await _context.ThirdPartyAttributions.AsNoTracking()
                    .SingleOrDefaultAsync(a => a.BookingId == bookingId && a.PartnerId == dto.PartnerId, cancellationToken);
            if (attribution == null || attribution.PartnerId != dto.PartnerId)
                throw new InvalidOperationException("Create a booking-level attribution for this partner before calculating commission.");

            var previousAmount = commission.FinalAmount;
            ResetCommissionCalculation(commission);
            commission.PartnerId = partner.Id;
            commission.AttributionId = attribution.Id;
            commission.PartnerNameSnapshot = partner.Name;
            commission.PartnerTypeSnapshot = partner.PartnerType;
            commission.PartnerInternalCodeSnapshot = partner.InternalCode;
            commission.AllocationPercentSnapshot = attribution.AllocationPercent;
            commission.IsManual = dto.IsManual;
            if (dto.IsManual) ApplyManualCalculation(commission, booking, dto);
            else await ApplyRuleCalculationAsync(commission, booking, partner, dto.RuleId, cancellationToken);
            ApplyCommissionAdjustment(commission, dto.AdjustmentAmount, dto.AdjustmentReason,
                dto.IsManual ? dto.ManualReason : null, booking);
            if (commission.FinalAmount <= 0m)
                throw new InvalidOperationException("Final commission must be greater than zero.");

            commission.Status = BookingCommissionStatus.Draft;
            commission.ApprovedAmount = null;
            commission.SubmittedAt = null;
            commission.SubmittedByUserId = null;
            commission.SubmittedByName = null;
            commission.DecisionAt = null;
            commission.DecisionByUserId = null;
            commission.DecisionByName = null;
            commission.DecisionReason = null;
            commission.UpdatedAt = DateTime.UtcNow;
            if (!commission.IsManual && !commission.RequiresApprovalSnapshot)
            {
                commission.Status = BookingCommissionStatus.Approved;
                commission.ApprovedAmount = commission.FinalAmount;
                commission.DecisionAt = DateTime.UtcNow;
                commission.DecisionByUserId = actor.UserId;
                commission.DecisionByName = actor.DisplayName;
                commission.DecisionReason = "Approval inherited from the applied rule after correction.";
            }

            Audit(FinancialWorkflowAction.CommissionAdjusted, actor, partner.Id, booking.CustomerId, bookingId,
                commission.Id, oldCommission: BookingCommissionStatus.Draft, newCommission: commission.Status,
                previousAmount: previousAmount, newAmount: commission.FinalAmount, reason: changeReason,
                commissionRuleId: commission.RuleId, commissionRuleRevisionId: commission.RuleRevisionId);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public async Task<BookingCommissionRebateWorkspaceDto> ChangeCommissionStatusAsync(int bookingId, int commissionId,
            CommissionStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var commission = await _context.BookingCommissions.Include(c => c.Booking).ThenInclude(b => b.Payments)
                .Include(c => c.Booking).ThenInclude(b => b.Installments)
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals).Include(c => c.Evidence)
                .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Commission not found for this booking.");
            ApplyToken(commission, dto.ConcurrencyToken, "commission");
            var previous = commission.Status;
            var reason = Limited(dto.Reason, "Reason", 2000);
            var action = FinancialWorkflowAction.CommissionAdjusted;
            switch (dto.TargetStatus)
            {
                case BookingCommissionStatus.PendingApproval when previous == BookingCommissionStatus.Draft:
                    if (commission.IsManual && commission.Evidence.Count == 0)
                        throw new InvalidOperationException("Manual commissions require supporting evidence before submission.");
                    commission.Status = BookingCommissionStatus.PendingApproval;
                    commission.SubmittedAt = DateTime.UtcNow; commission.SubmittedByUserId = actor.UserId; commission.SubmittedByName = actor.DisplayName;
                    action = FinancialWorkflowAction.CommissionSubmitted; break;
                case BookingCommissionStatus.Approved when previous == BookingCommissionStatus.PendingApproval:
                    var approved = Money(dto.ApprovedAmount ?? commission.FinalAmount);
                    if (approved <= 0m) throw new InvalidOperationException("Approved commission must be greater than zero.");
                    if (approved != commission.FinalAmount && reason == null)
                        throw new InvalidOperationException("A reason is required when the approved amount differs from the calculated amount.");
                    commission.ApprovedAmount = approved; commission.Status = BookingCommissionStatus.Approved;
                    commission.DecisionAt = DateTime.UtcNow; commission.DecisionByUserId = actor.UserId; commission.DecisionByName = actor.DisplayName;
                    commission.DecisionReason = reason; action = FinancialWorkflowAction.CommissionApproved; break;
                case BookingCommissionStatus.Rejected when previous == BookingCommissionStatus.PendingApproval:
                    RequireReason(reason, "A rejection reason is required."); commission.Status = BookingCommissionStatus.Rejected;
                    commission.DecisionAt = DateTime.UtcNow; commission.DecisionByUserId = actor.UserId; commission.DecisionByName = actor.DisplayName;
                    commission.DecisionReason = reason; action = FinancialWorkflowAction.CommissionRejected; break;
                case BookingCommissionStatus.Draft when previous == BookingCommissionStatus.PendingApproval:
                    RequireReason(reason, "A return reason is required."); commission.Status = BookingCommissionStatus.Draft;
                    commission.DecisionAt = DateTime.UtcNow; commission.DecisionByUserId = actor.UserId; commission.DecisionByName = actor.DisplayName;
                    commission.DecisionReason = reason; action = FinancialWorkflowAction.CommissionReturned; break;
                case BookingCommissionStatus.Earned when previous == BookingCommissionStatus.Approved:
                    EnsureCommissionEarned(commission, reason); commission.Status = BookingCommissionStatus.Earned;
                    commission.EarnedAt = DateTime.UtcNow; action = FinancialWorkflowAction.CommissionEarned; break;
                case BookingCommissionStatus.Payable when previous == BookingCommissionStatus.Earned:
                    commission.Status = BookingCommissionStatus.Payable; commission.PayableAt = DateTime.UtcNow;
                    action = FinancialWorkflowAction.CommissionPayable; break;
                case BookingCommissionStatus.Cancelled when previous is BookingCommissionStatus.Draft or BookingCommissionStatus.Approved
                    or BookingCommissionStatus.Earned or BookingCommissionStatus.Payable or BookingCommissionStatus.Rejected:
                    RequireReason(reason, "A cancellation reason is required.");
                    if (NetPaid(commission) > 0m) throw new InvalidOperationException("Paid commissions require payout reversal, not cancellation.");
                    commission.Status = BookingCommissionStatus.Cancelled; commission.CancellationOrReversalReason = reason;
                    action = FinancialWorkflowAction.CommissionCancelled; break;
                default:
                    throw new InvalidOperationException($"Commission cannot move from {previous} to {dto.TargetStatus}.");
            }
            commission.UpdatedAt = DateTime.UtcNow;
            Audit(action, actor, commission.PartnerId, commission.Booking.CustomerId, bookingId, commission.Id,
                oldCommission: previous, newCommission: commission.Status, previousAmount: commission.FinalAmount,
                newAmount: commission.ApprovedAmount ?? commission.FinalAmount, reason: reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public Task<BookingCommissionRebateWorkspaceDto> RecordPayoutAsync(int bookingId, int commissionId,
            RecordCommissionPayoutDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(async () =>
            {
                ValidateMovement(dto.Amount, dto.IdempotencyKey, dto.PaymentDate);
                if (!Enum.IsDefined(dto.PaymentMethod)) throw new InvalidOperationException("Select a valid payout payment method.");
                var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
                var paymentDate = dto.PaymentDate.Date;
                var paymentReference = Limited(dto.PaymentReference, "Payment reference", 200);
                var notes = Limited(dto.Notes, "Notes", 2000);
                if (dto.PaymentMethod != PaymentMethod.Cash && paymentReference == null)
                    throw new InvalidOperationException("A payment reference is required for non-cash payouts.");
                var existing = await _context.CommissionPayouts.AsNoTracking().Include(p => p.Commission)
                    .SingleOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken);
                if (existing != null)
                {
                    if (existing.CommissionId != commissionId || existing.Amount != Money(dto.Amount)
                        || existing.FinanceAccountId != dto.FinanceAccountId || existing.Commission.BookingId != bookingId
                        || existing.PaymentDate != paymentDate || existing.PaymentMethod != dto.PaymentMethod
                        || existing.PaymentReference != paymentReference || existing.Notes != notes)
                        throw new InvalidOperationException("This idempotency key was already used for a different payout.");
                    return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
                }
                // Uniqueness guards against recording the same live bank transaction twice. A fully
                // reversed payout/disbursement no longer represents money out, so its reference must be
                // reusable (e.g. reverse a mis-keyed entry, then re-record it correctly). Partially
                // reversed rows still hold a live balance and keep the reference blocked.
                if (paymentReference != null && (await _context.CommissionPayouts.AnyAsync(p =>
                        p.FinanceAccountId == dto.FinanceAccountId && p.PaymentReference == paymentReference
                        && p.Amount > p.Reversals.Sum(r => r.Amount), cancellationToken)
                    || await _context.RebateDisbursements.AnyAsync(d => d.FinanceAccountId == dto.FinanceAccountId
                        && d.Reference == paymentReference
                        && d.Amount > d.Reversals.Sum(r => r.Amount), cancellationToken)))
                    throw new InvalidOperationException("This payment reference is already recorded against the selected finance account.");
                var commission = await _context.BookingCommissions.Include(c => c.Booking)
                    .Include(c => c.Partner)
                    .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                    .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Commission not found for this booking.");
                ApplyToken(commission, dto.CommissionConcurrencyToken, "commission");
                EnsureActiveBooking(commission.Booking);
                if (!await _context.ThirdPartyPartners.AnyAsync(p => p.Id == commission.PartnerId && p.IsActive, cancellationToken))
                    throw new InvalidOperationException("This partner is inactive. Reactivate the partner before recording a payout.");
                if (dto.PaymentMethod == PaymentMethod.BankTransfer
                    && (string.IsNullOrWhiteSpace(commission.Partner.BankName)
                        || string.IsNullOrWhiteSpace(commission.Partner.AccountTitle)
                        || (string.IsNullOrWhiteSpace(commission.Partner.AccountNumber)
                            && string.IsNullOrWhiteSpace(commission.Partner.Iban))))
                    throw new InvalidOperationException("Bank-transfer payouts require the partner's bank name, account title, and account number or IBAN.");
                if (commission.Status is not (BookingCommissionStatus.Payable or BookingCommissionStatus.PartiallyPaid))
                    throw new InvalidOperationException("Only payable or partially-paid commissions can receive a payout.");
                await _financeAccounts.EnsureSelectableAsync(dto.FinanceAccountId, cancellationToken: cancellationToken);
                var outstanding = Money((commission.ApprovedAmount ?? commission.FinalAmount) - NetPaid(commission));
                var amount = Money(dto.Amount);
                if (amount > outstanding) throw new InvalidOperationException($"Payout exceeds the outstanding commission of {outstanding:0.00}.");
                var payout = new CommissionPayout
                {
                    CommissionId = commission.Id, FinanceAccountId = dto.FinanceAccountId, Amount = amount,
                    PaymentDate = paymentDate, PaymentMethod = dto.PaymentMethod,
                    PaymentReference = paymentReference,
                    DestinationBankNameSnapshot = commission.Partner.BankName,
                    DestinationAccountTitleSnapshot = commission.Partner.AccountTitle,
                    DestinationAccountNumberSnapshot = commission.Partner.AccountNumber,
                    DestinationIbanSnapshot = commission.Partner.Iban,
                    IdempotencyKey = idempotencyKey, Notes = notes,
                    RecordedByUserId = actor.UserId, RecordedByName = actor.DisplayName, RecordedAt = DateTime.UtcNow
                };
                _context.CommissionPayouts.Add(payout);
                var previous = commission.Status;
                commission.Status = amount == outstanding ? BookingCommissionStatus.Paid : BookingCommissionStatus.PartiallyPaid;
                commission.UpdatedAt = DateTime.UtcNow;
                var audit = Audit(FinancialWorkflowAction.PayoutRecorded, actor, commission.PartnerId, commission.Booking.CustomerId,
                    bookingId, commission.Id, oldCommission: previous,
                    newCommission: commission.Status, newAmount: amount, reason: payout.PaymentReference);
                audit.Payout = payout;
                await _context.SaveChangesAsync(cancellationToken);
                return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
            }, cancellationToken);

        public Task<BookingCommissionRebateWorkspaceDto> ReversePayoutAsync(int bookingId, int commissionId, int payoutId,
            ReverseMoneyMovementDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(async () =>
            {
                ValidateReversal(dto);
                var idempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80);
                var reason = Required(dto.Reason, "Reversal reason", 2000);
                var existing = await _context.CommissionPayoutReversals.AsNoTracking()
                    .Include(r => r.Payout).ThenInclude(p => p.Commission)
                    .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
                if (existing != null)
                {
                    if (existing.PayoutId != payoutId || existing.Amount != Money(dto.Amount)
                        || existing.Payout.CommissionId != commissionId || existing.Payout.Commission.BookingId != bookingId
                        || existing.Reason != reason)
                        throw new InvalidOperationException("This idempotency key was already used for a different reversal.");
                    return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
                }
                var commission = await _context.BookingCommissions.Include(c => c.Booking)
                    .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                    .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Commission not found for this booking.");
                var payout = commission.Payouts.SingleOrDefault(p => p.Id == payoutId)
                    ?? throw new KeyNotFoundException("Payout not found for this commission.");
                var amount = Money(dto.Amount); var available = Money(payout.Amount - payout.Reversals.Sum(r => r.Amount));
                if (amount > available) throw new InvalidOperationException($"Reversal exceeds the payout balance of {available:0.00}.");
                var reversal = new CommissionPayoutReversal
                {
                    PayoutId = payout.Id, Amount = amount, Reason = reason,
                    IdempotencyKey = idempotencyKey, ReversedByUserId = actor.UserId,
                    ReversedByName = actor.DisplayName, ReversedAt = DateTime.UtcNow
                };
                _context.CommissionPayoutReversals.Add(reversal);
                // NetPaid already reflects this reversal: EF relationship fixup adds it to
                // payout.Reversals the moment it is tracked. Subtracting `amount` again would
                // double-count and leave a fully reversed payout stuck in PartiallyPaid (and,
                // on a cancelled booking, in ReversalRequired instead of the terminal Reversed).
                var remainingPaid = NetPaid(commission);
                var previous = commission.Status;
                if (commission.Booking.Status == BookingStatus.Cancelled)
                    commission.Status = remainingPaid == 0m ? BookingCommissionStatus.Reversed : BookingCommissionStatus.ReversalRequired;
                else
                    commission.Status = remainingPaid == 0m ? BookingCommissionStatus.Payable : BookingCommissionStatus.PartiallyPaid;
                commission.UpdatedAt = DateTime.UtcNow;
                Audit(FinancialWorkflowAction.PayoutReversed, actor, commission.PartnerId, commission.Booking.CustomerId,
                    bookingId, commission.Id, payout.Id, oldCommission: previous, newCommission: commission.Status,
                    newAmount: amount, reason: dto.Reason);
                if (commission.Status == BookingCommissionStatus.Reversed)
                    Audit(FinancialWorkflowAction.CommissionReversed, actor, commission.PartnerId, commission.Booking.CustomerId,
                        bookingId, commission.Id, oldCommission: previous, newCommission: commission.Status, reason: dto.Reason);
                await _context.SaveChangesAsync(cancellationToken);
                return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
            }, cancellationToken);

        private async Task<Booking> LoadBookingForCalculationAsync(int bookingId, CancellationToken cancellationToken) =>
            await _context.Bookings.Include(b => b.Customer).Include(b => b.Unit).ThenInclude(u => u.Project)
                .Include(b => b.Payments).Include(b => b.Installments)
                .SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new KeyNotFoundException("Booking not found.");

        private async Task ApplyRuleCalculationAsync(BookingCommission commission, Booking booking, ThirdPartyPartner partner,
            int? explicitRuleId, CancellationToken cancellationToken)
        {
            var bookingDate = booking.BookingDate.Date;
            var rules = _context.CommissionRules.AsNoTracking().Where(r => r.IsActive
                    && r.EffectiveFrom <= bookingDate && (r.EffectiveTo == null || r.EffectiveTo >= bookingDate)
                    && (r.PartnerId == null || r.PartnerId == partner.Id)
                    && (r.PartnerType == null || r.PartnerType == partner.PartnerType)
                    && (r.ProjectId == null || r.ProjectId == booking.Unit.ProjectId)
                    && (r.UnitCategory == null || r.UnitCategory == booking.Unit.UnitType)
                    && (r.BookingSource == null || r.BookingSource == booking.Source)
                    && (r.BookingId == null || r.BookingId == booking.Id));
            CommissionRule? rule;
            if (explicitRuleId.HasValue)
            {
                rule = await rules.SingleOrDefaultAsync(r => r.Id == explicitRuleId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The selected rule is inactive, outside its effective dates, or does not apply to this booking and partner.");
            }
            else
            {
                var ranked = await rules.Select(r => new
                    {
                        Rule = r,
                        Score = (r.BookingId.HasValue ? 64 : 0) + (r.PartnerId.HasValue ? 32 : 0)
                            + (r.ProjectId.HasValue ? 16 : 0) + (r.UnitCategory != null ? 8 : 0)
                            + (r.BookingSource.HasValue ? 4 : 0) + (r.PartnerType != null ? 2 : 0)
                    })
                    .OrderByDescending(x => x.Rule.Priority).ThenByDescending(x => x.Score)
                    .ThenBy(x => x.Rule.Id).Take(2).ToListAsync(cancellationToken);
                if (ranked.Count == 0) throw new InvalidOperationException("No commission rule applies. Create a rule or use a documented manual commission.");
                var best = ranked[0];
                if (ranked.Skip(1).Any(x => x.Rule.Priority == best.Rule.Priority && x.Score == best.Score))
                    throw new InvalidOperationException("More than one equally ranked commission rule applies. Resolve the ambiguity or select a rule explicitly.");
                rule = best.Rule;
            }
            commission.RuleId = rule.Id; commission.RuleNameSnapshot = rule.Name; commission.CalculationType = rule.CalculationType;
            commission.RuleRevisionId = await _context.CommissionRuleRevisions.AsNoTracking()
                .Where(r => r.RuleId == rule.Id).OrderByDescending(r => r.RevisionNumber)
                .Select(r => (int?)r.Id).FirstOrDefaultAsync(cancellationToken);
            commission.PercentageRate = rule.PercentageRate; commission.FixedAmount = rule.FixedAmount;
            commission.CalculationBasis = rule.CalculationBasis; commission.BasisAmount = BasisAmount(booking, rule.CalculationBasis, null);
            var calculated = CalculateRaw(rule.CalculationType, commission.BasisAmount, rule.PercentageRate, rule.FixedAmount);
            if (rule.MinimumCommission.HasValue) calculated = Math.Max(calculated, rule.MinimumCommission.Value);
            if (rule.MaximumCommission.HasValue) calculated = Math.Min(calculated, rule.MaximumCommission.Value);
            commission.CalculatedAmount = Money(calculated * commission.AllocationPercentSnapshot / 100m);
            commission.EarningCondition = rule.EarningCondition;
            commission.MinimumCollectionPercent = rule.MinimumCollectionPercent;
            commission.RulePrioritySnapshot = rule.Priority; commission.MinimumCommissionSnapshot = rule.MinimumCommission;
            commission.MaximumCommissionSnapshot = rule.MaximumCommission;
            commission.EligibilityConditionSnapshot = rule.EligibilityCondition;
            commission.RequiresApprovalSnapshot = rule.RequiresApproval;
        }

        private static void ApplyManualCalculation(BookingCommission commission, Booking booking, CreateBookingCommissionDto dto)
        {
            commission.ManualReason = Required(dto.ManualReason, "Manual commission reason", 2000);
            commission.CalculationType = dto.ManualCalculationType
                ?? throw new InvalidOperationException("Manual calculation type is required.");
            commission.CalculationBasis = dto.ManualCalculationBasis
                ?? throw new InvalidOperationException("Manual calculation basis is required.");
            if (!Enum.IsDefined(commission.CalculationType) || !Enum.IsDefined(commission.CalculationBasis))
                throw new InvalidOperationException("Select a valid manual calculation type and basis.");
            commission.BasisAmount = BasisAmount(booking, commission.CalculationBasis, dto.ManualBasisAmount);
            if (commission.CalculationType == FinancialCalculationType.Percentage)
            {
                if (dto.ManualPercentageRate is not (> 0m and <= 100m) || dto.ManualFixedAmount.HasValue)
                    throw new InvalidOperationException("Manual percentage commission requires a rate between 0 and 100 and no fixed amount.");
                commission.PercentageRate = Rate(dto.ManualPercentageRate.Value);
            }
            else
            {
                if (dto.ManualFixedAmount is not > 0m || dto.ManualPercentageRate.HasValue)
                    throw new InvalidOperationException("Manual fixed commission requires a positive fixed amount and no percentage rate.");
                commission.FixedAmount = Money(dto.ManualFixedAmount.Value);
            }
            commission.CalculatedAmount = Money(CalculateRaw(commission.CalculationType, commission.BasisAmount,
                commission.PercentageRate, commission.FixedAmount) * commission.AllocationPercentSnapshot / 100m);
            commission.EarningCondition = dto.ManualEarningCondition ?? CommissionEarningCondition.ManualMilestone;
            if (!Enum.IsDefined(commission.EarningCondition))
                throw new InvalidOperationException("Select a valid commission earning condition.");
            commission.MinimumCollectionPercent = commission.EarningCondition == CommissionEarningCondition.MinimumCollectionPercentage
                ? Money(dto.MinimumCollectionPercent ?? 0m) : null;
            if (commission.EarningCondition == CommissionEarningCondition.MinimumCollectionPercentage
                && commission.MinimumCollectionPercent is not (> 0m and <= 100m))
                throw new InvalidOperationException("A collection percentage between 0 and 100 is required.");
        }

        private static void ApplyCommissionAdjustment(BookingCommission commission, decimal adjustment, string? reason,
            string? manualReason, Booking booking)
        {
            commission.AdjustmentAmount = Money(adjustment);
            commission.AdjustmentReason = Limited(reason, "Adjustment reason", 2000);
            if (commission.AdjustmentAmount != 0m && commission.AdjustmentReason == null)
                throw new InvalidOperationException("An adjustment reason is required.");
            commission.FinalAmount = Money(commission.CalculatedAmount + commission.AdjustmentAmount);
            if (commission.FinalAmount < 0m) throw new InvalidOperationException("The final commission cannot be negative.");
            var netPrice = Money(booking.AgreedSalePrice - booking.DiscountAmount);
            if (commission.FinalAmount > netPrice && commission.AdjustmentReason == null && string.IsNullOrWhiteSpace(manualReason))
                throw new InvalidOperationException("Commission above the net sale price requires an explicit justification.");
        }

        private static void ResetCommissionCalculation(BookingCommission commission)
        {
            commission.RuleId = null;
            commission.RuleRevisionId = null;
            commission.RuleNameSnapshot = null;
            commission.RulePrioritySnapshot = null;
            commission.MinimumCommissionSnapshot = null;
            commission.MaximumCommissionSnapshot = null;
            commission.EligibilityConditionSnapshot = null;
            commission.RequiresApprovalSnapshot = true;
            commission.ManualReason = null;
            commission.PercentageRate = null;
            commission.FixedAmount = null;
            commission.BasisAmount = 0m;
            commission.CalculatedAmount = 0m;
            commission.AdjustmentAmount = 0m;
            commission.AdjustmentReason = null;
            commission.FinalAmount = 0m;
            commission.MinimumCollectionPercent = null;
        }

        private static decimal BasisAmount(Booking booking, FinancialCalculationBasis basis, decimal? manual) => basis switch
        {
            FinancialCalculationBasis.AgreedSalePrice => PositiveBasis(booking.AgreedSalePrice),
            FinancialCalculationBasis.NetSalePriceAfterDiscount => PositiveBasis(booking.AgreedSalePrice - booking.DiscountAmount),
            FinancialCalculationBasis.BookingAmountReceived => PositiveBasis(booking.BookingAmountReceived),
            FinancialCalculationBasis.AmountActuallyCollected => PositiveBasis(booking.Payments.Sum(p => p.Amount)),
            FinancialCalculationBasis.ManuallyApprovedAmount => PositiveBasis(manual ?? 0m),
            _ => throw new InvalidOperationException("Calculation basis is invalid.")
        };

        private static decimal PositiveBasis(decimal value) => value > 0m ? Money(value)
            : throw new InvalidOperationException("The selected calculation basis has no positive amount.");

        private static decimal Calculate(FinancialCalculationType type, decimal basis, decimal? rate, decimal? fixedAmount) =>
            Money(CalculateRaw(type, basis, rate, fixedAmount));

        private static decimal CalculateRaw(FinancialCalculationType type, decimal basis, decimal? rate, decimal? fixedAmount) => type switch
        {
            FinancialCalculationType.Percentage when rate is > 0m => basis * rate.Value / 100m,
            FinancialCalculationType.FixedAmount when fixedAmount is > 0m => fixedAmount.Value,
            _ => throw new InvalidOperationException("Calculation inputs are invalid.")
        };

        private static void EnsureCommissionEarned(BookingCommission commission, string? reason)
        {
            if (!string.IsNullOrWhiteSpace(commission.EligibilityConditionSnapshot) && string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("Confirm the rule's eligibility condition with a milestone note before marking commission earned.");
            var booking = commission.Booking; var collected = booking.Payments.Sum(p => p.Amount);
            var eligible = commission.EarningCondition switch
            {
                CommissionEarningCondition.BookingAmountFullyReceived => booking.BookingAmountRequired > 0m
                    && booking.BookingAmountReceived >= booking.BookingAmountRequired,
                CommissionEarningCondition.MinimumCollectionPercentage => commission.MinimumCollectionPercent is > 0m
                    && collected * 100m / Math.Max(booking.AgreedSalePrice - booking.DiscountAmount, 0.01m) >= commission.MinimumCollectionPercent,
                CommissionEarningCondition.FirstInstallmentReceived => booking.Payments.Any(p => p.Type == PaymentType.Installment),
                CommissionEarningCondition.SaleCompleted => booking.Status == BookingStatus.SaleCompleted,
                CommissionEarningCondition.ManualMilestone => !string.IsNullOrWhiteSpace(reason),
                _ => false
            };
            if (!eligible) throw new InvalidOperationException(commission.EarningCondition == CommissionEarningCondition.ManualMilestone
                ? "A milestone confirmation reason is required." : "The configured earning condition has not been met.");
        }

        private static decimal NetPaid(BookingCommission commission) => Money(commission.Payouts.Sum(p => p.Amount - p.Reversals.Sum(r => r.Amount)));
        private static void EnsureActiveBooking(Booking booking)
        {
            if (booking.Status == BookingStatus.Cancelled) throw new InvalidOperationException("Cancelled bookings cannot create or pay commissions.");
        }
        private static void RequireReason(string? reason, string message) { if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException(message); }
        private static void ValidateMovement(decimal amount, string? idempotencyKey, DateTime date)
        {
            if (Money(amount) <= 0m) throw new InvalidOperationException("Amount must be greater than zero.");
            Required(idempotencyKey, "Idempotency key", 80);
            if (date == default) throw new InvalidOperationException("Transaction date is required.");
            if (date.Date > PakistanTime.Today) throw new InvalidOperationException("Transaction date cannot be in the future.");
        }
        private static void ValidateReversal(ReverseMoneyMovementDto dto)
        {
            if (Money(dto.Amount) <= 0m) throw new InvalidOperationException("Reversal amount must be greater than zero.");
            Required(dto.Reason, "Reversal reason", 2000); Required(dto.IdempotencyKey, "Idempotency key", 80);
        }
    }
}
