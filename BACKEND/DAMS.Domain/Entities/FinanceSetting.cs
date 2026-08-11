namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Single-row configuration for the Finance module. Kept as one seeded row (Id = 1) rather
    /// than a key/value table because there is a small, fixed set of settings and every one of
    /// them is typed.
    /// </summary>
    public class FinanceSetting
    {
        public const int SingletonId = 1;

        public int Id { get; set; } = SingletonId;

        /// <summary>Month the financial year opens on, 1–12. Pakistan's tax year runs
        /// 1 July – 30 June, which is the seeded default.</summary>
        public int FinancialYearStartMonth { get; set; } = 7;

        /// <summary>Set once the client's accountant has checked the seeded withholding rates
        /// against the current Finance Act. Until then the rate table shows an unverified banner,
        /// because the seeded figures are starting values, not legal advice.</summary>
        public DateTime? WhtRatesConfirmedAt { get; set; }

        public string? WhtRatesConfirmedByName { get; set; }

        public DateTime? UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }
}
