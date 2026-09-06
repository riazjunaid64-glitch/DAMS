namespace DAMS.Domain.Enums
{
    public enum FinanceAccountType
    {
        Cash = 1,
        Bank = 2,
        MobileWallet = 3,
        Other = 4,
        Liability = 5,
        Capital = 6,
        FixedAsset = 7,
        Receivable = 8,
        WorkInProgress = 9,

        /// <summary>
        /// Company money temporarily held by a staff member. It remains a debit-normal current
        /// asset while positive; a negative balance means the company owes that person.
        /// </summary>
        StaffFloat = 10
    }

    public enum FinanceSystemAccountRole
    {
        None = 0,
        TaxPayable = 1,
        CustomerRefundPayable = 2,

        /// <summary>
        /// Customer money received before the sale is recognised. A liability, not income: until
        /// possession the company owes the buyer either the unit or the money back.
        /// </summary>
        CustomerDeposits = 3,

        /// <summary>
        /// What buyers still owe on sales that HAVE been recognised. Raised at possession for the
        /// unpaid part of the net sale value, cleared by later collections and valid credits.
        /// </summary>
        CustomerReceivables = 4,

        /// <summary>
        /// What the company owes third-party partners on agreed booking commissions. Raised on the
        /// day the commission is agreed (the same day it becomes an expense), cleared when the
        /// payout is recorded, and released if the commission is cancelled or dies with its booking.
        /// </summary>
        CommissionPayable = 5
    }

    public static class AccountBalanceDirection
    {
        public static bool IsDebitNormal(FinanceAccountType type) => type switch
        {
            FinanceAccountType.Liability or FinanceAccountType.Capital => false,
            _ => true
        };

        public static bool IsCashLike(FinanceAccountType type) => type is
            FinanceAccountType.Cash or FinanceAccountType.Bank or
            FinanceAccountType.MobileWallet or FinanceAccountType.Other;

        public static bool CanPayExpense(FinanceAccountType type) =>
            IsCashLike(type) || type == FinanceAccountType.StaffFloat;

        public static decimal ToNormalBalance(FinanceAccountType type, decimal debit, decimal credit) =>
            IsDebitNormal(type) ? debit - credit : credit - debit;
    }
}
