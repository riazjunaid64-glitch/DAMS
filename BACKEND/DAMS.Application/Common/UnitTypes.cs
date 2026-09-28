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
        /// What a lead form offers. Parking space stays a unit type and is still accepted when a
        /// booking sends it, but the lead screens do not offer it.
        /// </summary>
        public static readonly IReadOnlyList<string> LeadApartmentTypes =
            Allowed.Where(t => t != "Parking space").ToArray();

        public const string UnrecognisedLeadApartmentType =
            "Apartment type must be Studio, 1 Bed, 2 Bed or 3 Bed.";

        /// <summary>
        /// Maps a stored or typed value onto the canonical list. Matching ignores case and
        /// everything that is not a letter or digit. Returns null when it does not match,
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

            // Letters and digits only. The data migration repeats this list in SQL
            // (Dams_CanonicalLeadApartmentType); a new synonym belongs in both.
            var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return compact switch
            {
                "studio" or "studioapartment" => "Studio",
                "1bed" or "1bedroom" or "1bedroomapartment" or "onebedroom" or "onebedroomapartment" or "1bhk" => "1 Bed",
                "2bed" or "2bedroom" or "2bedroomapartment" or "twobedroom" or "twobedroomapartment" or "2bhk" => "2 Bed",
                "3bed" or "3bedroom" or "3bedroomapartment" or "threebedroom" or "threebedroomapartment" or "3bhk" => "3 Bed",
                "parkingspace" or "parking" => "Parking space",
                _ => null
            };
        }

        /// <summary>
        /// The apartment type to store on a lead. Blank stays blank. A known synonym becomes the
        /// canonical value. Staff who send anything else are refused. An external enquiry
        /// (Facebook, Instagram, a website) is never refused: an unrecognised type is left empty,
        /// and the original answer stays on the submission.
        /// </summary>
        public static string? ForLead(string? value, bool externalEnquiry)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var canonical = Canonical(value);
            if (canonical != null)
                return canonical;

            if (externalEnquiry)
                return null;

            throw new InvalidOperationException(UnrecognisedLeadApartmentType);
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

        private static BusinessRuleException Rejected() =>
            new($"Unit type must be one of: {string.Join(", ", Allowed)}.");
    }
}
