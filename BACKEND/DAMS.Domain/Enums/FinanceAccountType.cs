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
        WorkInProgress = 9
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

        public static decimal ToNormalBalance(FinanceAccountType type, decimal debit, decimal credit) =>
            IsDebitNormal(type) ? debit - credit : credit - debit;
    }
}
