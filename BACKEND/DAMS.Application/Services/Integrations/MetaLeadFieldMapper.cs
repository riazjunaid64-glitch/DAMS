using System.Text.Json;
using DAMS.Application.DTOs.IntegrationDtos;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>What a form's answers resolved to, plus everything they did not.</summary>
    public sealed class MappedMetaFields
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? WhatsappNumber { get; set; }
        public string? Email { get; set; }
        public string? City { get; set; }

        /// <summary>Every answer, in order, each flagged with whether DAMS understood it.</summary>
        public List<ExternalFieldAnswerDto> AllAnswers { get; set; } = [];

        public string ToFieldDataJson() => JsonSerializer.Serialize(AllAnswers);
    }

    /// <summary>
    /// Translates Meta's form answers into the handful of fields a Lead actually has.
    ///
    /// The important half of this class is what it refuses to do. Lead forms ask whatever the
    /// advertiser wants — budgets, project names, timelines, free text — and inferring meaning
    /// from question wording would put wrong numbers into fields the sales team makes decisions
    /// with. So only an explicitly recognised question name maps to a column; everything else
    /// is preserved verbatim and shown on the lead, unmapped and honest.
    ///
    /// Adding a mapping later is a line in <see cref="Mappings"/>, not a change to how
    /// ingestion works.
    /// </summary>
    public static class MetaLeadFieldMapper
    {
        private enum Target { FirstName, LastName, FullName, Phone, Whatsapp, Email, City }

        private static readonly Dictionary<string, Target> Mappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["full_name"] = Target.FullName,
            ["name"] = Target.FullName,
            ["first_name"] = Target.FirstName,
            ["last_name"] = Target.LastName,
            ["phone_number"] = Target.Phone,
            ["phone"] = Target.Phone,
            ["whatsapp_number"] = Target.Whatsapp,
            ["whatsapp"] = Target.Whatsapp,
            ["email"] = Target.Email,
            ["city"] = Target.City,
            ["city_name"] = Target.City
        };

        public static MappedMetaFields Map(IEnumerable<MetaFieldAnswer> answers)
        {
            var mapped = new MappedMetaFields();

            foreach (var answer in answers)
            {
                var key = Normalize(answer.Name);
                var recognized = Mappings.TryGetValue(key, out var target);

                mapped.AllAnswers.Add(new ExternalFieldAnswerDto
                {
                    Name = answer.Name,
                    Value = answer.Value,
                    IsMapped = recognized
                });

                if (!recognized || string.IsNullOrWhiteSpace(answer.Value))
                    continue;

                var value = answer.Value.Trim();

                switch (target)
                {
                    case Target.FullName:
                        // Only fill from a full name when no dedicated first/last answer has
                        // already supplied one, so an explicit field always wins.
                        var (first, last) = SplitName(value);
                        mapped.FirstName ??= first;
                        mapped.LastName ??= last;
                        break;
                    case Target.FirstName:
                        mapped.FirstName = value;
                        break;
                    case Target.LastName:
                        mapped.LastName = value;
                        break;
                    case Target.Phone:
                        mapped.Phone ??= value;
                        break;
                    case Target.Whatsapp:
                        mapped.WhatsappNumber ??= value;
                        break;
                    case Target.Email:
                        mapped.Email ??= value;
                        break;
                    case Target.City:
                        mapped.City ??= value;
                        break;
                }
            }

            return mapped;
        }

        /// <summary>
        /// Meta question names arrive in several shapes ("full_name", "Full Name", "full name").
        /// Reducing them to lowercase words joined by underscores lets one table entry cover all
        /// of them without resorting to fuzzy matching.
        /// </summary>
        private static string Normalize(string name)
        {
            var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray());
            return string.Join('_', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static (string First, string? Last) SplitName(string value)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                0 => (value, null),
                1 => (parts[0], null),
                _ => (parts[0], string.Join(' ', parts[1..]))
            };
        }
    }
}
