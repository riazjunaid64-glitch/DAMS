using System.Text.Json;
using System.Text.Json.Serialization;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// Applies an administrator's mapping for one lead form to a submission's answers: the
    /// form's project, and the lead field each mapped question fills.
    ///
    /// It never guesses. A question is only used when the mapping names it, and an answer only
    /// when it is one of the options the mapping gives a value for; anything else stays an
    /// unmapped answer, shown on the lead exactly as it arrived. A form with no mapping changes
    /// nothing at all.
    /// </summary>
    public static class LeadFormAnswerMapper
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize(List<LeadFormAnswerMappingDto> answers) => JsonSerializer.Serialize(answers, Json);

        public static List<LeadFormAnswerMappingDto> ReadAnswers(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<LeadFormAnswerMappingDto>>(json, Json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <param name="questions">The form's synced questions, used only to recognise an answer sent as option text.</param>
        public static void Apply(
            MappedMetaFields mapped,
            ExternalLeadFormMapping? mapping,
            IReadOnlyCollection<LeadFormQuestionDto> questions)
        {
            if (mapping is null)
                return;

            mapped.InterestedProjectId = mapping.InterestedProjectId;

            var rules = ReadAnswers(mapping.AnswerMappingsJson);
            if (rules.Count == 0)
                return;

            foreach (var answer in mapped.AllAnswers)
            {
                if (string.IsNullOrWhiteSpace(answer.Value))
                    continue;

                var key = LeadFormQuestions.Normalize(answer.Name);
                var rule = rules.FirstOrDefault(r => LeadFormQuestions.Normalize(r.QuestionKey) == key);
                if (rule is null)
                    continue;

                var option = FindOption(rule, LeadFormQuestions.Find(questions, answer.Name), answer.Value);
                if (option is not null && Assign(mapped, rule.Target, option.Value))
                    answer.IsMapped = true;
            }
        }

        /// <summary>
        /// The mapped option an answer chose. Meta's export shows option keys, but the API has
        /// been reported to return the option's text instead, so both are accepted: the key, the
        /// text saved with the mapping, and the text the form sync last read.
        /// </summary>
        private static LeadFormOptionMappingDto? FindOption(
            LeadFormAnswerMappingDto rule, LeadFormQuestionDto? question, string answer)
        {
            var value = LeadFormQuestions.Normalize(answer);
            if (value.Length == 0)
                return null;

            var matched = rule.Options.FirstOrDefault(o => LeadFormQuestions.Normalize(o.OptionKey) == value)
                          ?? rule.Options.FirstOrDefault(o => o.OptionLabel is not null
                                                              && LeadFormQuestions.Normalize(o.OptionLabel) == value);
            if (matched is not null)
                return matched;

            var synced = LeadFormQuestions.FindOption(question, answer);
            if (synced is null)
                return null;

            var syncedKey = LeadFormQuestions.Normalize(synced.Key);
            return rule.Options.FirstOrDefault(o => LeadFormQuestions.Normalize(o.OptionKey) == syncedKey);
        }

        /// <summary>
        /// Fills the field unless an earlier answer already did. False when the value is not one
        /// the field accepts, so a mapping saved before a rename cannot write a wrong value.
        /// </summary>
        private static bool Assign(MappedMetaFields mapped, LeadFormAnswerTarget target, string value)
        {
            switch (target)
            {
                case LeadFormAnswerTarget.PropertyType:
                    var propertyType = value.Trim();
                    if (propertyType.Length == 0)
                        return false;
                    mapped.PropertyType ??= propertyType;
                    return true;

                case LeadFormAnswerTarget.PurchaseIntent:
                    if (!TryParseKnown<LeadPurchaseIntent>(value, out var intent))
                        return false;
                    if (mapped.PurchaseIntent == LeadPurchaseIntent.Unknown)
                        mapped.PurchaseIntent = intent;
                    return true;

                case LeadFormAnswerTarget.PaymentPreference:
                    if (!TryParseKnown<LeadPaymentPreference>(value, out var preference))
                        return false;
                    if (mapped.PaymentPreference == LeadPaymentPreference.Unknown)
                        mapped.PaymentPreference = preference;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>A named, defined value other than Unknown — never a number or a combination.</summary>
        public static bool TryParseKnown<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
        {
            result = default;
            var name = value?.Trim();
            return !string.IsNullOrEmpty(name)
                   && !char.IsDigit(name[0])
                   && Enum.TryParse(name, ignoreCase: true, out result)
                   && Enum.IsDefined(result)
                   && !result.Equals(default(TEnum));
        }
    }
}
