using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CommissionRebateDtos
{
    public sealed class CommissionRebateSummaryDto
    {
        public decimal AccruedCommission { get; set; }
        public decimal PayableCommission { get; set; }
        public decimal CommissionPaid { get; set; }
        public decimal CommissionReversalRequired { get; set; }
        public decimal ApprovedRebates { get; set; }
        public decimal RebatesAppliedOrPaid { get; set; }
        public decimal RebateReversalRequired { get; set; }
        public int ActivePartners { get; set; }
        public int PendingApprovals { get; set; }
    }

    public sealed class ThirdPartyPartnerDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PartnerType { get; set; } = string.Empty;
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Cnic { get; set; }
        public string? Ntn { get; set; }
        public string? RegistrationNumber { get; set; }
        public string InternalCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string? AccountTitle { get; set; }
        public string? AccountNumber { get; set; }
        public string? Iban { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public int AttributionCount { get; set; }
        public int CommissionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class SaveThirdPartyPartnerDto
    {
        public string Name { get; set; } = string.Empty;
        public string PartnerType { get; set; } = string.Empty;
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Cnic { get; set; }
        public string? Ntn { get; set; }
        public string? RegistrationNumber { get; set; }
        public string InternalCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string? AccountTitle { get; set; }
        public string? AccountNumber { get; set; }
        public string? Iban { get; set; }
        public string? Notes { get; set; }
        public string? ConcurrencyToken { get; set; }
    }

    public sealed class SetPartnerStatusDto
    {
        public bool IsActive { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public sealed class ThirdPartyAttributionDto
    {
        public int Id { get; set; }
        public int PartnerId { get; set; }
        public string PartnerName { get; set; } = string.Empty;
        public int? LeadId { get; set; }
        public int? CustomerId { get; set; }
        public int? BookingId { get; set; }
        public string RelationshipType { get; set; } = string.Empty;
        public DateTime? IntroducedAt { get; set; }
        public string? SourceDetails { get; set; }
        public string? Notes { get; set; }
        public bool IsPrimary { get; set; }
        public decimal AllocationPercent { get; set; }
        public DateTime AssignedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class SaveThirdPartyAttributionDto
    {
        public int PartnerId { get; set; }
        public int? LeadId { get; set; }
        public int? CustomerId { get; set; }
        public int? BookingId { get; set; }
        public string RelationshipType { get; set; } = "Introducer";
        public DateTime? IntroducedAt { get; set; }
        public string? SourceDetails { get; set; }
        public string? Notes { get; set; }
        public bool IsPrimary { get; set; }
        public decimal AllocationPercent { get; set; } = 100m;
        public string? ConcurrencyToken { get; set; }
    }

    public sealed class CommissionRuleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? PartnerId { get; set; }
        public string? PartnerName { get; set; }
        public string? PartnerType { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string? UnitCategory { get; set; }
        public CustomerSource? BookingSource { get; set; }
        public int? BookingId { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal? MinimumCommission { get; set; }
        public decimal? MaximumCommission { get; set; }
        public string? EligibilityCondition { get; set; }
        public CommissionEarningCondition EarningCondition { get; set; }
        public decimal? MinimumCollectionPercent { get; set; }
        public int Priority { get; set; }
        public bool RequiresApproval { get; set; }
        public string? Notes { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
        public int CurrentRevisionNumber { get; set; }
    }

    public sealed class SaveCommissionRuleDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public int? PartnerId { get; set; }
        public string? PartnerType { get; set; }
        public int? ProjectId { get; set; }
        public string? UnitCategory { get; set; }
        public CustomerSource? BookingSource { get; set; }
        public int? BookingId { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal? MinimumCommission { get; set; }
        public decimal? MaximumCommission { get; set; }
        public string? EligibilityCondition { get; set; }
        public CommissionEarningCondition EarningCondition { get; set; }
        public decimal? MinimumCollectionPercent { get; set; }
        public int Priority { get; set; }
        public bool RequiresApproval { get; set; } = true;
        public string? Notes { get; set; }
        public string? ConcurrencyToken { get; set; }
        public string? ChangeReason { get; set; }
    }

    public class CreateBookingCommissionDto
    {
        public int PartnerId { get; set; }
        public int? AttributionId { get; set; }
        public int? RuleId { get; set; }
        public bool IsManual { get; set; }
        public string? ManualReason { get; set; }
        public FinancialCalculationType? ManualCalculationType { get; set; }
        public FinancialCalculationBasis? ManualCalculationBasis { get; set; }
        public decimal? ManualPercentageRate { get; set; }
        public decimal? ManualFixedAmount { get; set; }
        public decimal? ManualBasisAmount { get; set; }
        public CommissionEarningCondition? ManualEarningCondition { get; set; }
        public decimal? MinimumCollectionPercent { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
    }

    public sealed class UpdateBookingCommissionDto : CreateBookingCommissionDto
    {
        public string ConcurrencyToken { get; set; } = string.Empty;
        public string ChangeReason { get; set; } = string.Empty;
    }

    public sealed class CommissionStatusChangeDto
    {
        public BookingCommissionStatus TargetStatus { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public string? Reason { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class RecordCommissionPayoutDto
    {
        public int FinanceAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string CommissionConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class ReverseMoneyMovementDto
    {
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    public class CreateCustomerRebateDto
    {
        public FinancialCalculationType CalculationType { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public decimal? ManualBasisAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
        public string Reason { get; set; } = string.Empty;
        public CustomerRebateMethod Method { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class UpdateCustomerRebateDto : CreateCustomerRebateDto
    {
        public string ConcurrencyToken { get; set; } = string.Empty;
        public string ChangeReason { get; set; } = string.Empty;
    }

    public sealed class RebateStatusChangeDto
    {
        public CustomerRebateStatus TargetStatus { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public string? Reason { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class RecordRebateDisbursementDto
    {
        public CustomerRebateMethod Method { get; set; }
        public int? FinanceAccountId { get; set; }
        public int? InstallmentId { get; set; }
        public decimal Amount { get; set; }
        public DateTime AppliedAt { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? Reference { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string RebateConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class MoneyMovementDto
    {
        public int Id { get; set; }
        public int? FinanceAccountId { get; set; }
        public string? FinanceAccountName { get; set; }
        public int? InstallmentId { get; set; }
        public CustomerRebateMethod? RebateMethod { get; set; }
        public decimal Amount { get; set; }
        public decimal ReversedAmount { get; set; }
        public DateTime Date { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
        public List<FinancialEvidenceDto> Evidence { get; set; } = [];
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class FinancialEvidenceDto
    {
        public int Id { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public sealed class FinancialAuditDto
    {
        public long Id { get; set; }
        public FinancialWorkflowAction Action { get; set; }
        public BookingCommissionStatus? PreviousCommissionStatus { get; set; }
        public BookingCommissionStatus? NewCommissionStatus { get; set; }
        public CustomerRebateStatus? PreviousRebateStatus { get; set; }
        public CustomerRebateStatus? NewRebateStatus { get; set; }
        public decimal? PreviousAmount { get; set; }
        public decimal? NewAmount { get; set; }
        public string? Reason { get; set; }
        public string? PerformedByName { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public sealed class BookingCommissionDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public int PartnerId { get; set; }
        public string PartnerName { get; set; } = string.Empty;
        public int? AttributionId { get; set; }
        public int? RuleId { get; set; }
        public int? RuleRevisionId { get; set; }
        public int? RuleRevisionNumber { get; set; }
        public string? RuleNameSnapshot { get; set; }
        public int? RulePriority { get; set; }
        public bool IsManual { get; set; }
        public string? ManualReason { get; set; }
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal BasisAmount { get; set; }
        public decimal AllocationPercent { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public decimal RecoveryRequiredAmount { get; set; }
        public CommissionEarningCondition EarningCondition { get; set; }
        public decimal? MinimumCollectionPercent { get; set; }
        public BookingCommissionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? SubmittedByName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? DecisionByName { get; set; }
        public DateTime? DecisionAt { get; set; }
        public string? DecisionReason { get; set; }
        public DateTime? EarnedAt { get; set; }
        public DateTime? PayableAt { get; set; }
        public string? CancellationOrReversalReason { get; set; }
        public List<MoneyMovementDto> Payouts { get; set; } = [];
        public List<FinancialEvidenceDto> Evidence { get; set; } = [];
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class CustomerRebateDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public FinancialCalculationType CalculationType { get; set; }
        public decimal? PercentageRate { get; set; }
        public decimal? FixedAmount { get; set; }
        public FinancialCalculationBasis CalculationBasis { get; set; }
        public decimal BasisAmount { get; set; }
        public decimal CalculatedAmount { get; set; }
        public decimal AdjustmentAmount { get; set; }
        public string? AdjustmentReason { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public decimal AppliedOrPaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public decimal RecoveryRequiredAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public CustomerRebateMethod Method { get; set; }
        public CustomerRebateStatus Status { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? SubmittedByName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? DecisionByName { get; set; }
        public DateTime? DecisionAt { get; set; }
        public string? DecisionReason { get; set; }
        public string? CancellationOrReversalReason { get; set; }
        public List<MoneyMovementDto> Disbursements { get; set; } = [];
        public List<FinancialEvidenceDto> Evidence { get; set; } = [];
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public sealed class BookingCommissionRebateWorkspaceDto
    {
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public BookingStatus BookingStatus { get; set; }
        public decimal AgreedSalePrice { get; set; }
        public decimal NetSalePrice { get; set; }
        public decimal AmountCollected { get; set; }
        public decimal RebateCredits { get; set; }
        public List<ThirdPartyAttributionDto> Attributions { get; set; } = [];
        public List<BookingCommissionDto> Commissions { get; set; } = [];
        public List<CustomerRebateDto> Rebates { get; set; } = [];
        /// <summary>The most recent audit entries only; page the full log via the audit endpoint when <see cref="HasMoreAudit"/> is true.</summary>
        public List<FinancialAuditDto> Audit { get; set; } = [];
        public bool HasMoreAudit { get; set; }
    }

    public sealed class FinancialEvidenceUpload
    {
        public required Stream Content { get; init; }
        public required string FileName { get; init; }
        public required long Length { get; init; }
    }

    public sealed class FinancialEvidenceDownload
    {
        public required Stream Content { get; init; }
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
    }
}
