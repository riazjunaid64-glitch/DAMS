namespace DAMS.Application.Common
{
    /// <summary>
    /// Resolves the financial year a date falls in. Pakistan's tax year runs 1 July – 30 June,
    /// but the start month is configurable, so nothing here hard-codes July.
    /// </summary>
    public static class FinancialYear
    {
        public const int DefaultStartMonth = 7;

        /// <summary>
        /// The half-open window [Start, EndExclusive) containing <paramref name="date"/>.
        /// Half-open so range filters need no "end of day" fudging.
        /// </summary>
        public static (DateTime Start, DateTime EndExclusive) Window(DateTime date, int startMonth)
        {
            var month = Normalise(startMonth);
            var year = date.Month >= month ? date.Year : date.Year - 1;
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return (start, start.AddYears(1));
        }

        /// <summary>Label for the year containing <paramref name="date"/>, e.g. "2025-26".
        /// A January–December year is labelled with the single calendar year instead.</summary>
        public static string Label(DateTime date, int startMonth)
        {
            var (start, _) = Window(date, startMonth);
            return Normalise(startMonth) == 1
                ? start.Year.ToString()
                : $"{start.Year}-{(start.Year + 1) % 100:00}";
        }

        /// <summary>Clamps a stored/user-supplied month back into 1–12, falling back to July.
        /// A corrupt setting must not be able to throw inside a tax calculation.</summary>
        public static int Normalise(int startMonth) =>
            startMonth is >= 1 and <= 12 ? startMonth : DefaultStartMonth;
    }
}
