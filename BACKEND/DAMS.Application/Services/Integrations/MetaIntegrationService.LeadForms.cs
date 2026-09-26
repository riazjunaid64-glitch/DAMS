using DAMS.Application.Common;
using DAMS.Application.DTOs.IntegrationDtos;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Integrations
{
    /// <summary>
    /// What an administrator says a lead form means: the project its leads are about, and the
    /// lead field each of its questions fills. Applied by MetaLeadEventProcessor to submissions
    /// that arrive after it is saved.
    /// </summary>
    public sealed partial class MetaIntegrationService
    {
        private const string FormMappingConflict =
            "This form's mapping was changed by someone else. Reload it and try again.";

        public async Task<LeadFormMappingDto> GetLeadFormMappingAsync(
            string formExternalId, CancellationToken cancellationToken = default)
        {
            var form = await LoadLeadFormAsync(formExternalId, cancellationToken);

            var mapping = await _context.ExternalLeadFormMappings
                .AsNoTracking()
                .Where(m => m.Provider == IntegrationProviders.Meta && m.FormExternalId == form.ExternalId)
                .Select(m => new
                {
                    Mapping = m,
                    ProjectName = m.InterestedProject != null ? m.InterestedProject.ProjectName : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            return new LeadFormMappingDto
            {
                FormExternalId = form.ExternalId,
                FormName = form.Name,
                Questions = form.Questions,
                InterestedProjectId = mapping?.Mapping.InterestedProjectId,
                InterestedProjectName = mapping?.ProjectName,
                Answers = LeadFormAnswerMapper.ReadAnswers(mapping?.Mapping.AnswerMappingsJson),
                Version = mapping is null ? null : Convert.ToBase64String(mapping.Mapping.RowVersion),
                UpdatedAt = mapping?.Mapping.UpdatedAt ?? mapping?.Mapping.CreatedAt
            };
        }

        public async Task<LeadFormMappingDto> SaveLeadFormMappingAsync(
            string formExternalId, SaveLeadFormMappingDto dto, LeadUserContext actor,
            CancellationToken cancellationToken = default)
        {
            var form = await LoadLeadFormAsync(formExternalId, cancellationToken);

            if (dto.InterestedProjectId.HasValue
                && !await _context.Projects.AnyAsync(p => p.Id == dto.InterestedProjectId.Value, cancellationToken))
                throw new InvalidOperationException("That project no longer exists.");

            var answers = ValidateAnswers(dto.Answers ?? [], form.Questions);

            var existing = await _context.ExternalLeadFormMappings
                .FirstOrDefaultAsync(m => m.Provider == IntegrationProviders.Meta
                                          && m.FormExternalId == form.ExternalId, cancellationToken);

            if (existing is null)
            {
                // Removed since it was loaded.
                if (!string.IsNullOrWhiteSpace(dto.Version))
                    throw new LeadConcurrencyException(FormMappingConflict);
            }
            else if (string.IsNullOrWhiteSpace(dto.Version))
            {
                // Opened before this mapping existed, so it has not seen what it would replace.
                if (existing.RowVersion is { Length: > 0 })
                    throw new LeadConcurrencyException(FormMappingConflict);
            }
            else
            {
                try
                {
                    _context.Entry(existing).Property(m => m.RowVersion).OriginalValue =
                        Convert.FromBase64String(dto.Version);
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException("The mapping version is invalid. Reload it and try again.");
                }
            }

            // Nothing left to say about the form: it goes back to behaving exactly as an
            // unmapped form, which is simplest to guarantee with no row at all.
            if (!dto.InterestedProjectId.HasValue && answers.Count == 0)
            {
                if (existing is not null)
                {
                    _context.ExternalLeadFormMappings.Remove(existing);
                    await SaveFormMappingAsync(cancellationToken);
                }

                return await GetLeadFormMappingAsync(form.ExternalId, cancellationToken);
            }

            var now = DateTime.UtcNow;
            if (existing is null)
            {
                existing = new ExternalLeadFormMapping
                {
                    Provider = IntegrationProviders.Meta,
                    FormExternalId = form.ExternalId,
                    CreatedAt = now
                };
                _context.ExternalLeadFormMappings.Add(existing);
            }
            else
            {
                existing.UpdatedAt = now;
            }

            existing.InterestedProjectId = dto.InterestedProjectId;
            existing.AnswerMappingsJson = answers.Count == 0 ? null : LeadFormAnswerMapper.Serialize(answers);
            existing.UpdatedByUserId = actor.UserId;

            await SaveFormMappingAsync(cancellationToken);

            return await GetLeadFormMappingAsync(form.ExternalId, cancellationToken);
        }

        /// <summary>
        /// Only questions and options the synced form actually has can be mapped, and they are
        /// stored under the form's own keys and with its own option text — never what the
        /// browser sent — so a typo cannot become a rule that silently never matches.
        /// </summary>
        private static List<LeadFormAnswerMappingDto> ValidateAnswers(
            List<LeadFormAnswerMappingDto> answers, List<LeadFormQuestionDto> questions)
        {
            if (answers.Count == 0)
                return [];

            if (questions.Count == 0)
                throw new InvalidOperationException(
                    "DAMS has not read this form's questions yet. Run Sync now on its Meta connection, then map its answers.");

            var validated = new List<LeadFormAnswerMappingDto>();
            var usedQuestions = new HashSet<string>();
            var usedTargets = new HashSet<LeadFormAnswerTarget>();

            foreach (var answer in answers)
            {
                var question = LeadFormQuestions.Find(questions, answer.QuestionKey ?? string.Empty)
                    ?? throw new InvalidOperationException($"This form has no question \"{answer.QuestionKey}\".");
                var questionName = question.Label ?? question.Key;

                if (!Enum.IsDefined(answer.Target))
                    throw new InvalidOperationException($"Choose which lead field \"{questionName}\" fills.");
                if (!usedQuestions.Add(question.Key))
                    throw new InvalidOperationException($"\"{questionName}\" is mapped more than once.");
                if (!usedTargets.Add(answer.Target))
                    throw new InvalidOperationException($"Only one question can fill {TargetLabel(answer.Target)}.");

                var options = new List<LeadFormOptionMappingDto>();
                var usedOptions = new HashSet<string>();
                foreach (var option in answer.Options ?? [])
                {
                    var formOption = question.Options.FirstOrDefault(o =>
                            LeadFormQuestions.Normalize(o.Key) == LeadFormQuestions.Normalize(option.OptionKey ?? string.Empty))
                        ?? throw new InvalidOperationException(
                            $"\"{questionName}\" has no option \"{option.OptionKey}\".");

                    if (!usedOptions.Add(formOption.Key))
                        throw new InvalidOperationException(
                            $"\"{formOption.Value ?? formOption.Key}\" is mapped more than once.");

                    options.Add(new LeadFormOptionMappingDto
                    {
                        OptionKey = formOption.Key,
                        OptionLabel = formOption.Value,
                        Value = ValidateValue(answer.Target, option.Value, formOption.Value ?? formOption.Key)
                    });
                }

                if (options.Count == 0)
                    throw new InvalidOperationException($"Give at least one answer to \"{questionName}\" a value, or remove it.");

                validated.Add(new LeadFormAnswerMappingDto { QuestionKey = question.Key, Target = answer.Target, Options = options });
            }

            return validated;
        }

        private static string ValidateValue(LeadFormAnswerTarget target, string? value, string optionName)
        {
            var trimmed = value?.Trim() ?? string.Empty;

            return target switch
            {
                LeadFormAnswerTarget.PropertyType when trimmed.Length is > 0 and <= 100 => trimmed,
                LeadFormAnswerTarget.PropertyType =>
                    throw new InvalidOperationException($"Enter a property type of up to 100 characters for \"{optionName}\"."),
                LeadFormAnswerTarget.PurchaseIntent when LeadFormAnswerMapper.TryParseKnown<LeadPurchaseIntent>(trimmed, out var intent) =>
                    intent.ToString(),
                LeadFormAnswerTarget.PaymentPreference when LeadFormAnswerMapper.TryParseKnown<LeadPaymentPreference>(trimmed, out var preference) =>
                    preference.ToString(),
                _ => throw new InvalidOperationException($"Choose a {TargetLabel(target)} for \"{optionName}\".")
            };
        }

        private static string TargetLabel(LeadFormAnswerTarget target) => target switch
        {
            LeadFormAnswerTarget.PropertyType => "property type",
            LeadFormAnswerTarget.PurchaseIntent => "purchase intent",
            LeadFormAnswerTarget.PaymentPreference => "payment preference",
            _ => "lead field"
        };

        private async Task SaveFormMappingAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new LeadConcurrencyException(FormMappingConflict);
            }
            catch (DbUpdateException ex) when (IsDuplicateFormMapping(ex))
            {
                // Two administrators saved a first mapping for the same form at once.
                throw new LeadConcurrencyException(FormMappingConflict);
            }
        }

        private static bool IsDuplicateFormMapping(DbUpdateException ex) =>
            ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_ExternalLeadFormMappings_Provider_FormExternalId", StringComparison.OrdinalIgnoreCase);

        private sealed record LeadForm(string ExternalId, string? Name, List<LeadFormQuestionDto> Questions);

        /// <summary>A form any connection has discovered, with its most recently synced name and questions.</summary>
        private async Task<LeadForm> LoadLeadFormAsync(string formExternalId, CancellationToken cancellationToken)
        {
            var id = formExternalId?.Trim() ?? string.Empty;

            var rows = await _context.ExternalIntegrationResources
                .AsNoTracking()
                .Where(r => r.Provider == IntegrationProviders.Meta
                            && r.ResourceType == ExternalResourceTypes.LeadForm
                            && r.ExternalId == id)
                .OrderByDescending(r => r.LastSyncedAt)
                .Select(r => new { r.ExternalId, r.Name, r.MetadataJson })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                throw new LeadNotFoundException("DAMS has not discovered that lead form. Run Sync now on its Meta connection.");

            return new LeadForm(
                rows[0].ExternalId,
                rows.Select(r => r.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                LeadFormQuestions.Read(rows.Select(r => r.MetadataJson).FirstOrDefault(m => m != null)));
        }
    }
}
