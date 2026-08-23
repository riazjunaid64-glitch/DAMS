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

    /// <summary>
    /// A commission is owed from the moment it is agreed and is Pending until it is fully paid,
    /// whether or not part of it has already gone out — how much is paid and how much is left comes
    /// from the payout rows, not from the status. Cancelled voids one that was never paid; the
    /// reversal pair is only reached when a booking is cancelled after money has already left.
    /// </summary>
    public enum BookingCommissionStatus
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2,
        ReversalRequired = 3,
        Reversed = 4
    }

    /// <summary>
    /// The rebate mirror of <see cref="BookingCommissionStatus"/>. It ends in Applied when it was
    /// given as a credit against what the customer owes, and in Paid when it was actually paid out.
    /// </summary>
    public enum CustomerRebateStatus
    {
        Pending = 0,
        Applied = 1,
        Paid = 2,
        Cancelled = 3,
        ReversalRequired = 4,
        Reversed = 5
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
        RebateAdjusted = 35,
        BookingCancellationSettlementRecorded = 36,
        BookingCancellationRefundPaid = 37
    }

    public enum FinancialEvidenceOwnerType
    {
        Commission = 0,
        CommissionPayout = 1,
        Rebate = 2,
        RebateDisbursement = 3
    }
}
