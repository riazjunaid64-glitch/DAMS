namespace DAMS.Domain.Entities
{
    /// <summary>
    /// Apartment-type answers on a saved Facebook form mapping that the canonical list
    /// could not read. The mapping keeps the old text; this row is how the owner finds it.
    /// No foreign key, so the note outlives the mapping.
    /// </summary>
    public class LeadFormApartmentTypeMigrationNote
    {
        public int Id { get; set; }

        public int ExternalLeadFormMappingId { get; set; }

        public string Provider { get; set; } = string.Empty;

        public string FormExternalId { get; set; } = string.Empty;

        public string QuestionKey { get; set; } = string.Empty;

        public string OptionKey { get; set; } = string.Empty;

        /// <summary>The value as it was stored. Left unchanged on the mapping itself.</summary>
        public string Value { get; set; } = string.Empty;

        public DateTime NotedAt { get; set; }
    }
}
