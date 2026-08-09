namespace DAMS.Domain.Enums
{
    public enum FinancialCalculationType
    {
        Percentage = 0,
        FixedAmount = 1
    }

    public enum FinancialCalculationBasis
    {
        AgreedSalePrice = 0,
        NetSalePriceAfterDiscount = 1,
        BookingAmountReceived = 2,
        AmountActuallyCollected = 3,
        ManuallyApprovedAmount = 4
    }

    public enum CommissionEarningCondition
    {
        ManualMilestone = 0,
        BookingAmountFullyReceived = 1,
        MinimumCollectionPercentage = 2,
        FirstInstallmentReceived = 3,
        SaleCompleted = 4
    }

    public enum BookingCommissionStatus
    {
        Draft = 0,
        PendingApproval = 1,
        Approved = 2,
        Earned = 3,
        Payable = 4,
        PartiallyPaid = 5,
        Paid = 6,
        Rejected = 7,
        Cancelled = 8,
        ReversalRequired = 9,
        Reversed = 10
    }

    public enum CustomerRebateStatus
    {
        Draft = 0,
        PendingApproval = 1,
        Approved = 2,
        PartiallyApplied = 3,
        Applied = 4,
        Paid = 5,
        Rejected = 6,
        Cancelled = 7,
        Reversed = 8,
        ReversalRequired = 9
    }

    public enum CustomerRebateMethod
    {
        OutstandingBalanceReduction = 0,
        InstallmentAdjustment = 1,
        CashOrBankPayment = 2,
        CreditNote = 3,
        Other = 4
    }

    public enum FinancialWorkflowAction
    {
        PartnerCreated = 0,
        PartnerUpdated = 1,
        PartnerActivated = 2,
        PartnerDeactivated = 3,
        PartnerAssigned = 4,
        PartnerAttributionUpdated = 5,
        RuleCreated = 6,
        RuleUpdated = 7,
        CommissionCreated = 8,
        CommissionCalculated = 9,
        CommissionAdjusted = 10,
        CommissionSubmitted = 11,
        CommissionApproved = 12,
        CommissionRejected = 13,
        CommissionReturned = 14,
        CommissionEarned = 15,
        CommissionPayable = 16,
        PayoutRecorded = 17,
        PayoutReversed = 18,
        CommissionCancelled = 19,
        CommissionReversalRequired = 20,
        CommissionReversed = 21,
        RebateCreated = 22,
        RebateSubmitted = 23,
        RebateApproved = 24,
        RebateRejected = 25,
        RebateReturned = 26,
        RebateApplied = 27,
        RebatePaid = 28,
        RebateCancelled = 29,
        RebateReversed = 30,
        RebateDisbursementReversed = 31,
        EvidenceUploaded = 32,
        EvidenceDownloaded = 33,
        RebateReversalRequired = 34,
        RebateAdjusted = 35
    }

    public enum FinancialEvidenceOwnerType
    {
        Commission = 0,
        CommissionPayout = 1,
        Rebate = 2,
        RebateDisbursement = 3
    }
}
