using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// The withholding rules, with no database and no clock. Everything about how much tax is
    /// withheld lives here so it can be reasoned about — and tested — on its own.
    /// </summary>
    public static class WhtCalculator
    {
        /// <summary>Rate table inputs for one category, already loaded.</summary>
        public readonly record struct CategoryRates(
            bool IsWhtApplicable,
            decimal FilerRate,
            decimal NonFilerRate,
            decimal AnnualThreshold);

        public sealed class Result
        {
            public bool IsWhtApplicable { get; init; }
            public decimal Rate { get; init; }
            public decimal WhtAmount { get; init; }
            public decimal NetPaid { get; init; }
            public bool WhtApplied { get; init; }

            /// <summary>True when the annual threshold has not been reached yet, so nothing is
            /// withheld even though the head is otherwise taxable.</summary>
            public bool BelowThreshold { get; init; }

            public decimal AnnualThreshold { get; init; }
            public decimal YearToDateTotal { get; init; }
        }

        /// <summary>
        /// The rate that applies to a vendor. Unknown filer status is treated as non-filer:
        /// deducting too little is what carries a penalty, deducting too much is refunded to the
        /// vendor when they file.
        /// </summary>
        public static decimal RateFor(CategoryRates category, FilerStatus filerStatus) =>
            filerStatus == FilerStatus.Filer ? category.FilerRate : category.NonFilerRate;

        /// <summary>
        /// Tax on a single payment, rounded to paisa. Half-up, because the .NET default is
        /// banker's rounding and a tax figure that rounds 2.5 down to 2 is an under-deduction.
        /// </summary>
        public static decimal TaxOn(decimal grossAmount, decimal rate) =>
            Math.Round(grossAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The suggested withholding for one payment.
        /// </summary>
        /// <param name="yearToDateTotal">Gross already paid to this vendor, this financial year,
        /// under the same tax section — excluding the payment being entered.</param>
        public static Result Compute(
            CategoryRates category,
            FilerStatus filerStatus,
            decimal grossAmount,
            decimal yearToDateTotal)
        {
            if (!category.IsWhtApplicable)
            {
                return new Result
                {
                    IsWhtApplicable = false,
                    Rate = 0m,
                    WhtAmount = 0m,
                    NetPaid = grossAmount,
                    WhtApplied = false,
                    BelowThreshold = false,
                    AnnualThreshold = category.AnnualThreshold,
                    YearToDateTotal = yearToDateTotal
                };
            }

            var rate = RateFor(category, filerStatus);

            // The statutory threshold is an annual aggregate per vendor, not a per-invoice test:
            // a supplier paid 40,000 twice has crossed a 75,000 threshold even though neither
            // payment did on its own. Once crossed, the whole payment is withheld — earlier
            // under-threshold payments are left alone rather than retro-adjusted, which would
            // rewrite periods that may already be filed.
            var belowThreshold = category.AnnualThreshold > 0m
                && yearToDateTotal + grossAmount <= category.AnnualThreshold;

            var whtAmount = belowThreshold ? 0m : TaxOn(grossAmount, rate);

            return new Result
            {
                IsWhtApplicable = true,
                Rate = rate,
                WhtAmount = whtAmount,
                // Derived by subtraction, never rounded separately, or gross ≠ tax + net and the
                // account balance stops reconciling.
                NetPaid = grossAmount - whtAmount,
                WhtApplied = whtAmount > 0m,
                BelowThreshold = belowThreshold,
                AnnualThreshold = category.AnnualThreshold,
                YearToDateTotal = yearToDateTotal
            };
        }

        /// <summary>
        /// The rate a hand-entered tax amount implies, so an operator who types the figure from a
        /// vendor invoice still leaves a percentage behind for the s.165 statement.
        /// </summary>
        public static decimal EffectiveRate(decimal grossAmount, decimal whtAmount) =>
            grossAmount <= 0m ? 0m : Math.Round(whtAmount * 100m / grossAmount, 4, MidpointRounding.AwayFromZero);
    }
}
