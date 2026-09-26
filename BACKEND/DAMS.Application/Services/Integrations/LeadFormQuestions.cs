using System.Text.Json;
using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// A lead form's questions as the provider describes them, stored on the form's resource
    /// row (ExternalIntegrationResource.MetadataJson) by the form sync.
    ///
    /// They are only ever used to describe answers — the question's wording, the option's
    /// text — and to match an answer sent as text rather than as an option key. What a lead
    /// actually said is always the stored answer, never this.
    /// </summary>
    public static class LeadFormQuestions
    {
        public static string Serialize(List<LeadFormQuestionDto> questions) => JsonSerializer.Serialize(questions);

        /// <summary>The stored questions, or none. Malformed metadata is not worth failing a lead over.</summary>
        public static List<LeadFormQuestionDto> Read(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<LeadFormQuestionDto>>(metadataJson) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>
        /// The most recently synced questions of each form, keyed by the form's id. A form seen
        /// through more than one connection has a resource row per connection; the latest wins.
        /// </summary>
        public static async Task<Dictionary<string, List<LeadFormQuestionDto>>> LoadAsync(
            AppDbContext context, string provider, IReadOnlyCollection<string> formIds,
            CancellationToken cancellationToken)
        {
            if (formIds.Count == 0)
                return [];

            var rows = await context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => r.Provider == provider
                            && r.ResourceType == ExternalResourceTypes.LeadForm
                            && formIds.Contains(r.ExternalId)
                            && r.MetadataJson != null)
                .Select(r => new { r.ExternalId, r.MetadataJson, r.LastSyncedAt })
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(r => r.ExternalId)
                .ToDictionary(
                    g => g.Key,
                    g => Read(g.OrderByDescending(r => r.LastSyncedAt).First().MetadataJson));
        }

        public static LeadFormQuestionDto? Find(IEnumerable<LeadFormQuestionDto> questions, string questionKey)
        {
            var key = Normalize(questionKey);
            return questions.FirstOrDefault(q => Normalize(q.Key) == key);
        }

        /// <summary>
        /// The option an answer chose, whether the provider sent the option's key
        /// ("personal_living") or its text ("Personal Living").
        /// </summary>
        public static LeadFormOptionDto? FindOption(LeadFormQuestionDto? question, string? answer)
        {
            if (question is null || string.IsNullOrWhiteSpace(answer))
                return null;

            var value = Normalize(answer);
            if (value.Length == 0)
                return null;

            return question.Options.FirstOrDefault(o => Normalize(o.Key) == value)
                   ?? question.Options.FirstOrDefault(o => o.Value is not null && Normalize(o.Value) == value);
        }

        /// <summary>Adds the question's wording and the chosen option's text, leaving the answer itself untouched.</summary>
        public static void Describe(IEnumerable<ExternalFieldAnswerDto> answers, IReadOnlyCollection<LeadFormQuestionDto> questions)
        {
            if (questions.Count == 0)
                return;

            foreach (var answer in answers)
            {
                var question = Find(questions, answer.Name);
                if (question is null)
                    continue;

                answer.Label = string.IsNullOrWhiteSpace(question.Label) ? null : question.Label;
                answer.ValueLabel = FindOption(question, answer.Value)?.Value;
            }
        }

        /// <summary>
        /// Reduces keys and texts to lowercase words joined by underscores, so "no_(_on_cash)",
        /// "No (on cash)" and "NO ON CASH" are the same option without any fuzzy matching.
        /// </summary>
        public static string Normalize(string value) => MetaLeadFieldMapper.Normalize(value);
    }
}
