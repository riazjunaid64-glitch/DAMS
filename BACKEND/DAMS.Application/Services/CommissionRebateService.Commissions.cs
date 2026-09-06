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
            // Uniqueness applies only to the live commission for this partner: a cancelled or reversed
            // one is a closed historical record and must not block a corrected replacement.
            if (await _context.BookingCommissions.AnyAsync(c => c.BookingId == bookingId && c.PartnerId == dto.PartnerId
                    && c.Status != BookingCommissionStatus.Cancelled
                    && c.Status != BookingCommissionStatus.Reversed, cancellationToken))
                throw new InvalidOperationException("This partner already has a commission on this booking. Edit it, or cancel it and add a replacement.");
            var partner = await _context.ThirdPartyPartners.AsNoTracking().SingleOrDefaultAsync(p => p.Id == dto.PartnerId, cancellationToken)
                ?? throw new InvalidOperationException("Partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Inactive partners cannot receive new commissions.");
            var attribution = await ResolveAttributionAsync(bookingId, dto.PartnerId, dto.AttributionId, cancellationToken);

            var commission = new BookingCommission
            {
                BookingId = bookingId, PartnerId = partner.Id, AttributionId = attribution?.Id,
                PartnerNameSnapshot = partner.Name, PartnerTypeSnapshot = partner.PartnerType,
                PartnerInternalCodeSnapshot = partner.InternalCode,
                AllocationPercentSnapshot = attribution?.AllocationPercent ?? 100m,
                IsManual = dto.IsManual, CreatedByUserId = actor.UserId, CreatedByName = actor.DisplayName,
                // Agreeing the commission is the decision, so it is owed — Pending — from this moment,
                // and the record moves itself to Paid once the payouts cover it.
                CreatedAt = DateTime.UtcNow, Status = BookingCommissionStatus.Pending
            };
            if (dto.IsManual) ApplyManualCalculation(commission, booking, dto);
            else await ApplyRuleCalculationAsync(commission, booking, partner, dto.RuleId, cancellationToken);
            ApplyCommissionAdjustment(commission, dto.AdjustmentAmount, dto.AdjustmentReason,
                dto.IsManual ? dto.ManualReason : null, booking);
            if (commission.FinalAmount <= 0m)
                throw new InvalidOperationException("Final commission must be greater than zero.");
            _context.BookingCommissions.Add(commission);
            // Agreeing the commission is what makes it a cost and a liability, so that is the day
            // the formal statements have to see. Waiting for the payout left the P&L silent about a
            // commission already owed and the Balance Sheet silent about the payable.
            Accrue(commission, commission.FinalAmount, CommissionAccrualKind.Recognition, actor,
                commission.IsManual ? CommissionNarrative(commission) : commission.RuleNameSnapshot);
            var audit = Audit(FinancialWorkflowAction.CommissionCreated, actor, partner.Id, booking.CustomerId, bookingId,
                newCommission: commission.Status, newAmount: commission.FinalAmount,
                reason: commission.IsManual ? CommissionNarrative(commission) : commission.RuleNameSnapshot);
            audit.Commission = commission;
            var calculationAudit = Audit(FinancialWorkflowAction.CommissionCalculated, actor, partner.Id,
                booking.CustomerId, bookingId, newAmount: commission.CalculatedAmount,
                reason: commission.RuleNameSnapshot ?? CommissionNarrative(commission), commissionRuleId: commission.RuleId,
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
            // A pending commission is still just an agreement, so it can be corrected in place. Once
            // any money has moved the figures are history: reverse the payout to unwind it instead.
            if (commission.Status != BookingCommissionStatus.Pending)
                throw new InvalidOperationException("Only a pending commission can be edited.");
            if (commission.Payouts.Count != 0)
                throw new InvalidOperationException("A commission with payout history cannot be edited.");

            var changeReason = Required(dto.ChangeReason, "Change reason", 2000);
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            if (await _context.BookingCommissions.AnyAsync(c => c.Id != commissionId && c.BookingId == bookingId
                    && c.PartnerId == dto.PartnerId && c.Status != BookingCommissionStatus.Cancelled
                    && c.Status != BookingCommissionStatus.Reversed, cancellationToken))
                throw new InvalidOperationException("This partner already has a commission on this booking.");
            var partner = await _context.ThirdPartyPartners.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == dto.PartnerId, cancellationToken)
                ?? throw new InvalidOperationException("Partner not found.");
            if (!partner.IsActive) throw new InvalidOperationException("Inactive partners cannot receive new commissions.");
            var attribution = await ResolveAttributionAsync(bookingId, dto.PartnerId, dto.AttributionId, cancellationToken);

            var previousAmount = commission.FinalAmount;
            ResetCommissionCalculation(commission);
            commission.PartnerId = partner.Id;
            commission.AttributionId = attribution?.Id;
            commission.PartnerNameSnapshot = partner.Name;
            commission.PartnerTypeSnapshot = partner.PartnerType;
            commission.PartnerInternalCodeSnapshot = partner.InternalCode;
            commission.AllocationPercentSnapshot = attribution?.AllocationPercent ?? 100m;
            commission.IsManual = dto.IsManual;
            if (dto.IsManual) ApplyManualCalculation(commission, booking, dto);
            else await ApplyRuleCalculationAsync(commission, booking, partner, dto.RuleId, cancellationToken);
            ApplyCommissionAdjustment(commission, dto.AdjustmentAmount, dto.AdjustmentReason,
                dto.IsManual ? dto.ManualReason : null, booking);
            if (commission.FinalAmount <= 0m)
                throw new InvalidOperationException("Final commission must be greater than zero.");

            // A correction leaves the commission where it was — pending, for the corrected amount —
            // so only the figures and the audit trail change.
            commission.UpdatedAt = DateTime.UtcNow;

            // The obligation moves by the DIFFERENCE, dated today. Restating the original accrual
            // would rewrite the period it was first reported in; a signed correction leaves that
            // period alone and puts the change where it was actually decided.
            Accrue(commission, Money(commission.FinalAmount - previousAmount),
                CommissionAccrualKind.Adjustment, actor, changeReason);

            Audit(FinancialWorkflowAction.CommissionAdjusted, actor, partner.Id, booking.CustomerId, bookingId,
                commission.Id, oldCommission: commission.Status, newCommission: commission.Status,
                previousAmount: previousAmount, newAmount: commission.FinalAmount, reason: changeReason,
                commissionRuleId: commission.RuleId, commissionRuleRevisionId: commission.RuleRevisionId);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public async Task<BookingCommissionRebateWorkspaceDto> ChangeCommissionStatusAsync(int bookingId, int commissionId,
            CommissionStatusChangeDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var commission = await _context.BookingCommissions.Include(c => c.Booking)
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Commission not found for this booking.");
            ApplyToken(commission, dto.ConcurrencyToken, "commission");
            var previous = commission.Status;
            var reason = Limited(dto.Reason, "Reason", 2000);
            FinancialWorkflowAction action;
            switch (dto.TargetStatus)
            {
                // Cancelling is the only status a person still chooses. Everything else the record
                // decides for itself: it is pending from entry, and payouts move it to Paid. Money
                // that has already left is history, so a paid commission is unwound by reversing the
                // payout — which is also what puts it back to pending if it needs re-doing.
                case BookingCommissionStatus.Cancelled when previous == BookingCommissionStatus.Pending:
                    RequireReason(reason, "A cancellation reason is required.");
                    if (NetPaid(commission) > 0m) throw new InvalidOperationException("Part of this commission has already been paid. Reverse the payment before cancelling it.");
                    commission.Status = BookingCommissionStatus.Cancelled; commission.CancellationOrReversalReason = reason;
                    // Nothing is owed any more, so the payable and the expense come back off — on
                    // today's date, not by unwinding the period the commission was agreed in.
                    await ReleaseAccrualAsync(commission, actor, reason!, cancellationToken);
                    action = FinancialWorkflowAction.CommissionCancelled; break;
                default:
                    throw new InvalidOperationException($"Commission cannot move from {previous} to {dto.TargetStatus}.");
            }
            commission.UpdatedAt = DateTime.UtcNow;
            Audit(action, actor, commission.PartnerId, commission.Booking.CustomerId, bookingId, commission.Id,
                oldCommission: previous, newCommission: commission.Status, previousAmount: commission.FinalAmount,
                newAmount: commission.FinalAmount, reason: reason);
            await _context.SaveChangesAsync(cancellationToken);
            return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
        }

        public Task<BookingCommissionRebateWorkspaceDto> RecordPayoutAsync(int bookingId, int commissionId,
            RecordCommissionPayoutDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default) =>
            SerializableAsync(async () =>
            {
                await ValidateMovementAsync(dto.Amount, dto.IdempotencyKey, dto.PaymentDate, cancellationToken);
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
                        && d.Amount > d.Reversals.Sum(r => r.Amount), cancellationToken)
                    || await _context.BookingCancellationRefunds.AnyAsync(r => r.FinanceAccountId == dto.FinanceAccountId
                        && r.PaymentReference == paymentReference, cancellationToken)))
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
                if (commission.Status != BookingCommissionStatus.Pending)
                    throw new InvalidOperationException("Only a pending commission can receive a payout.");
                await _financeAccounts.EnsureSelectableAsync(dto.FinanceAccountId, cancellationToken: cancellationToken);
                var outstanding = Money(commission.FinalAmount - NetPaid(commission));
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
                // A part payment leaves it Pending: what is paid and what is left comes from the
                // payout rows, so there is no half-paid status to keep in step with them.
                commission.Status = amount == outstanding ? BookingCommissionStatus.Paid : BookingCommissionStatus.Pending;
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
                    ReversedByName = actor.DisplayName, ReversedAt = PakistanTime.Now
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
                    commission.Status = BookingCommissionStatus.Pending;
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

        // Attribution is optional. The booking screen records what a partner is owed on this booking
        // directly, so a commission stands on its own and is calculated in full. An attribution is a
        // separate lead-source record; when a caller names one the commission is still linked to it
        // and its allocation still splits the amount, which is what the shared-introduction flow needs.
        private async Task<ThirdPartyAttribution?> ResolveAttributionAsync(int bookingId, int partnerId,
            int? attributionId, CancellationToken cancellationToken)
        {
            if (!attributionId.HasValue) return null;
            var attribution = await _context.ThirdPartyAttributions.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == attributionId.Value && a.BookingId == bookingId, cancellationToken)
                ?? throw new InvalidOperationException("The selected attribution does not belong to this booking.");
            if (attribution.PartnerId != partnerId)
                throw new InvalidOperationException("The selected attribution belongs to a different partner.");
            return attribution;
        }

        private static string CommissionNarrative(BookingCommission commission) => commission.ManualReason
            ?? DescribeCalculation(commission.CalculationType, commission.PercentageRate,
                commission.FixedAmount, commission.CalculationBasis);

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
            commission.RulePrioritySnapshot = rule.Priority; commission.MinimumCommissionSnapshot = rule.MinimumCommission;
            commission.MaximumCommissionSnapshot = rule.MaximumCommission;
        }

        private static void ApplyManualCalculation(BookingCommission commission, Booking booking, CreateBookingCommissionDto dto)
        {
            // Optional: a commission agreed directly with a partner is self-explanatory from its own
            // rate and basis. A note is still stored when one is given, and is still what justifies an
            // amount above the net sale price (see ApplyCommissionAdjustment).
            commission.ManualReason = Limited(dto.ManualReason, "Commission note", 2000);
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
                throw new InvalidOperationException($"This commission of {commission.FinalAmount:0.00} is above the booking's net sale price of {netPrice:0.00}. Add a note explaining why before saving.");
        }

        private static void ResetCommissionCalculation(BookingCommission commission)
        {
            commission.RuleId = null;
            commission.RuleRevisionId = null;
            commission.RuleNameSnapshot = null;
            commission.RulePrioritySnapshot = null;
            commission.MinimumCommissionSnapshot = null;
            commission.MaximumCommissionSnapshot = null;
            commission.ManualReason = null;
            commission.PercentageRate = null;
            commission.FixedAmount = null;
            commission.BasisAmount = 0m;
            commission.CalculatedAmount = 0m;
            commission.AdjustmentAmount = 0m;
            commission.AdjustmentReason = null;
            commission.FinalAmount = 0m;
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

        /// <summary>
        /// Records one signed movement of the commission obligation, dated today.
        /// <para>
        /// The date is <see cref="PakistanTime.Today"/> rather than a caller-supplied one, and is
        /// deliberately not run through <see cref="FinanceDateRules"/>: today can never be in the
        /// future, and the go-live date can never be in the future either
        /// (<see cref="FinanceDateRules.EnsureBaselineDate"/>), so today is always on or after the
        /// committed baseline. There is nothing for the rule to reject and no reason to pay for the
        /// query. Zero movements are skipped — a row that moves nothing is noise the reports would
        /// still have to read.
        /// </para>
        /// </summary>
        private void Accrue(BookingCommission commission, decimal amount, CommissionAccrualKind kind,
            FinancialWorkflowActor actor, string? reason)
        {
            var value = Money(amount);
            if (value == 0m) return;
            _context.CommissionAccruals.Add(new CommissionAccrual
            {
                Commission = commission,
                Amount = value,
                AccruedOn = PakistanTime.Today,
                Kind = kind,
                Reason = Limited(reason, "Accrual reason", 2000),
                RecordedByUserId = actor.UserId,
                RecordedByName = Limited(actor.DisplayName, "Actor name", 200)
            });
        }

        /// <summary>
        /// Takes the whole remaining obligation back off the books. Reads the accrued balance from
        /// the ledger rather than assuming it equals <c>FinalAmount</c>: an edited commission has
        /// adjustment rows, and one already released (a cancelled booking, then the commission
        /// cancelled) must not be released twice.
        /// </summary>
        private async Task ReleaseAccrualAsync(BookingCommission commission, FinancialWorkflowActor actor,
            string reason, CancellationToken cancellationToken)
        {
            var accrued = Money(await _context.CommissionAccruals.AsNoTracking()
                .Where(a => a.CommissionId == commission.Id)
                .SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0m);
            // Rows added in this same unit of work are not in the database yet. Only the ADDED ones
            // are counted here — a tracked row already saved is inside the query above, and adding
            // it twice would release more than was ever accrued.
            accrued += Money(_context.ChangeTracker.Entries<CommissionAccrual>()
                .Where(e => e.State == EntityState.Added
                    && (ReferenceEquals(e.Entity.Commission, commission) || e.Entity.CommissionId == commission.Id))
                .Sum(e => e.Entity.Amount));
            Accrue(commission, -accrued, CommissionAccrualKind.Release, actor, reason);
        }

        private static decimal NetPaid(BookingCommission commission) => Money(commission.Payouts.Sum(p => p.Amount - p.Reversals.Sum(r => r.Amount)));
        private static void EnsureActiveBooking(Booking booking)
        {
            if (booking.Status == BookingStatus.Cancelled) throw new InvalidOperationException("Cancelled bookings cannot create or pay commissions.");
        }
        private static void RequireReason(string? reason, string message) { if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException(message); }
        // The date bounds are shared with every other financial posting date (FinanceDateRules), so a
        // payout or rebate cannot be dated into a period the opening balances already cover.
        private async Task ValidateMovementAsync(
            decimal amount, string? idempotencyKey, DateTime date, CancellationToken cancellationToken)
        {
            if (Money(amount) <= 0m) throw new InvalidOperationException("Amount must be greater than zero.");
            Required(idempotencyKey, "Idempotency key", 80);
            if (date == default) throw new InvalidOperationException("Transaction date is required.");
            await FinanceDateRules.EnsureAsync(_context, date, "Transaction date", cancellationToken);
        }
        private static void ValidateReversal(ReverseMoneyMovementDto dto)
        {
            if (Money(dto.Amount) <= 0m) throw new InvalidOperationException("Reversal amount must be greater than zero.");
            Required(dto.Reason, "Reversal reason", 2000); Required(dto.IdempotencyKey, "Idempotency key", 80);
        }
    }
}
