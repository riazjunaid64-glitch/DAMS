using DAMS.Application.Common;
using DAMS.Application.DTOs.CommissionRebateDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public sealed partial class CommissionRebateService
    {
        public async Task<List<BookingCommissionDto>> GetCommissionsAsync(BookingCommissionStatus? status,
            int? partnerId, int? projectId, CancellationToken cancellationToken = default)
        {
            var query = _context.BookingCommissions.AsNoTracking()
                .Include(c => c.Booking).ThenInclude(b => b.Unit)
                .Include(c => c.Partner).Include(c => c.Payouts).ThenInclude(p => p.FinanceAccount)
                .Include(c => c.Payouts).ThenInclude(p => p.Reversals).Include(c => c.Evidence).AsSplitQuery().AsQueryable();
            if (status.HasValue) query = query.Where(c => c.Status == status.Value);
            if (partnerId.HasValue) query = query.Where(c => c.PartnerId == partnerId.Value);
            if (projectId.HasValue) query = query.Where(c => c.Booking.Unit.ProjectId == projectId.Value);
            var rows = await query.OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(cancellationToken);
            return rows.Select(c => MapCommission(c)).ToList();
        }

        public async Task<BookingCommissionRebateWorkspaceDto> CreateCommissionAsync(int bookingId,
            CreateBookingCommissionDto dto, FinancialWorkflowActor actor, CancellationToken cancellationToken = default)
        {
            var booking = await LoadBookingForCalculationAsync(bookingId, cancellationToken);
            EnsureActiveBooking(booking);
            if (await _context.BookingCommissions.AnyAsync(c => c.BookingId == bookingId && c.PartnerId == dto.PartnerId, cancellationToken))
                throw new InvalidOperationException("A commission already exists for this partner and booking.");
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
                var existing = await _context.CommissionPayouts.AsNoTracking().Include(p => p.Commission)
                    .SingleOrDefaultAsync(p => p.IdempotencyKey == dto.IdempotencyKey, cancellationToken);
                if (existing != null)
                {
                    if (existing.CommissionId != commissionId || existing.Amount != Money(dto.Amount)
                        || existing.FinanceAccountId != dto.FinanceAccountId || existing.Commission.BookingId != bookingId)
                        throw new InvalidOperationException("This idempotency key was already used for a different payout.");
                    return await GetBookingWorkspaceAsync(bookingId, cancellationToken);
                }
                var commission = await _context.BookingCommissions.Include(c => c.Booking)
                    .Include(c => c.Partner)
                    .Include(c => c.Payouts).ThenInclude(p => p.Reversals)
                    .SingleOrDefaultAsync(c => c.Id == commissionId && c.BookingId == bookingId, cancellationToken)
                    ?? throw new KeyNotFoundException("Commission not found for this booking.");
                ApplyToken(commission, dto.CommissionConcurrencyToken, "commission");
                EnsureActiveBooking(commission.Booking);
                if (!await _context.ThirdPartyPartners.AnyAsync(p => p.Id == commission.PartnerId && p.IsActive, cancellationToken))
                    throw new InvalidOperationException("This partner is inactive. Reactivate the partner before recording a payout.");
                if (commission.Status is not (BookingCommissionStatus.Payable or BookingCommissionStatus.PartiallyPaid))
                    throw new InvalidOperationException("Only payable or partially-paid commissions can receive a payout.");
                await _financeAccounts.EnsureSelectableAsync(dto.FinanceAccountId, cancellationToken: cancellationToken);
                var outstanding = Money((commission.ApprovedAmount ?? commission.FinalAmount) - NetPaid(commission));
                var amount = Money(dto.Amount);
                if (amount > outstanding) throw new InvalidOperationException($"Payout exceeds the outstanding commission of {outstanding:0.00}.");
                var payout = new CommissionPayout
                {
                    CommissionId = commission.Id, FinanceAccountId = dto.FinanceAccountId, Amount = amount,
                    PaymentDate = dto.PaymentDate, PaymentMethod = dto.PaymentMethod,
                    PaymentReference = Limited(dto.PaymentReference, "Payment reference", 200),
                    DestinationBankNameSnapshot = commission.Partner.BankName,
                    DestinationAccountTitleSnapshot = commission.Partner.AccountTitle,
                    DestinationAccountNumberSnapshot = commission.Partner.AccountNumber,
                    DestinationIbanSnapshot = commission.Partner.Iban,
                    IdempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80), Notes = Limited(dto.Notes, "Notes", 2000),
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
                var existing = await _context.CommissionPayoutReversals.AsNoTracking()
                    .Include(r => r.Payout).ThenInclude(p => p.Commission)
                    .SingleOrDefaultAsync(r => r.IdempotencyKey == dto.IdempotencyKey, cancellationToken);
                if (existing != null)
                {
                    if (existing.PayoutId != payoutId || existing.Amount != Money(dto.Amount)
                        || existing.Payout.CommissionId != commissionId || existing.Payout.Commission.BookingId != bookingId)
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
                    PayoutId = payout.Id, Amount = amount, Reason = Required(dto.Reason, "Reversal reason", 2000),
                    IdempotencyKey = Required(dto.IdempotencyKey, "Idempotency key", 80), ReversedByUserId = actor.UserId,
                    ReversedByName = actor.DisplayName, ReversedAt = DateTime.UtcNow
                };
                _context.CommissionPayoutReversals.Add(reversal);
                var remainingPaid = Money(NetPaid(commission) - amount);
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
            var rules = _context.CommissionRules.AsNoTracking().Where(r => r.IsActive
                    && r.EffectiveFrom <= booking.BookingDate && (r.EffectiveTo == null || r.EffectiveTo >= booking.BookingDate)
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
            commission.BasisAmount = BasisAmount(booking, commission.CalculationBasis, dto.ManualBasisAmount);
            if (commission.CalculationType == FinancialCalculationType.Percentage)
            {
                if (dto.ManualPercentageRate is not (> 0m and <= 100m) || dto.ManualFixedAmount.HasValue)
                    throw new InvalidOperationException("Manual percentage commission requires a rate between 0 and 100 and no fixed amount.");
                commission.PercentageRate = dto.ManualPercentageRate;
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
            commission.MinimumCollectionPercent = dto.MinimumCollectionPercent;
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
        }
        private static void ValidateReversal(ReverseMoneyMovementDto dto)
        {
            if (Money(dto.Amount) <= 0m) throw new InvalidOperationException("Reversal amount must be greater than zero.");
            Required(dto.Reason, "Reversal reason", 2000); Required(dto.IdempotencyKey, "Idempotency key", 80);
        }
    }
}
