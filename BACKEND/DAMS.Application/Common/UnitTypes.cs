namespace DAMS.Application.Common
{
    /// <summary>
    /// The only unit types the redesigned screens offer. Stored text must be one of these.
    /// </summary>
    public static class UnitTypes
    {
        public static readonly IReadOnlyList<string> Allowed = new[]
        {
            "Studio", "1 Bed", "2 Bed", "3 Bed", "Parking space"
        };

        /// <summary>
        /// Maps a stored or typed value onto the canonical list. Returns null when it does not match,
        /// including values we deliberately leave for a person to fix.
        /// </summary>
        public static string? Canonical(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var exact = Allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return compact switch
            {
                "studio" => "Studio",
                "1bed" or "1bedroom" or "1bhk" => "1 Bed",
                "2bed" or "2bedroom" or "2bhk" => "2 Bed",
                "3bed" or "3bedroom" or "3bhk" => "3 Bed",
                "parkingspace" or "parking" => "Parking space",
                _ => null
            };
        }

        public static string Require(string? value)
        {
            var canonical = Canonical(value);
            if (canonical == null)
                throw Rejected();
            return canonical;
        }

        /// <summary>
        /// An edit may keep a legacy type that the migration could not map, so the rest of the
        /// unit can still be saved. A change to anything outside the fixed list is rejected.
        /// </summary>
        public static string ResolveForUpdate(string? submitted, string current)
        {
            var canonical = Canonical(submitted);
            if (canonical != null)
                return canonical;

            if (!string.IsNullOrWhiteSpace(current)
                && string.Equals(submitted?.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase))
                return current;

            throw Rejected();
        }

        private static InvalidOperationException Rejected() =>
            new($"Unit type must be one of: {string.Join(", ", Allowed)}.");
    }
}
