using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// An enquiry from an external channel whose contact details match more than one open lead —
    /// for example its phone matches one lead and its email another. Enriching either would join
    /// two people's records on a guess, so the enquiry is kept here, exactly as it arrived, until
    /// an administrator decides which lead it belongs to.
    /// </summary>
    public class LeadIntakeHold
    {
        public int Id { get; set; }

        /// <summary>Provider and its id for the submission, so a replay finds this hold instead of
        /// creating a second one. Null for a channel that sends no id of its own.</summary>
        public string? Provider { get; set; }

        public string? ExternalLeadId { get; set; }

        /// <summary>The enquiry as received, serialized, so resolving it later adds exactly what
        /// arrived — nothing is lost while it waits.</summary>
        public string PayloadJson { get; set; } = string.Empty;

        /// <summary>Whether the enquiry came through a trusted external channel, which decides
        /// whether resolving it records an external submission receipt.</summary>
        public bool IsExternal { get; set; }

        /// <summary>The provider's attribution for the submission (page, campaign, ad, form answers),
        /// serialized, for channels that supply it. Written onto the receipt when it is resolved.</summary>
        public string? AttributionJson { get; set; }

        /// <summary>The open leads the details matched when the enquiry arrived, comma separated.</summary>
        public string CandidateLeadIds { get; set; } = string.Empty;

        /// <summary>A website booking request waiting on this decision for its lead.</summary>
        public int? BookingRequestId { get; set; }

        public LeadIntakeHoldStatus Status { get; set; } = LeadIntakeHoldStatus.Open;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public int? ResolvedLeadId { get; set; }

        public int? ResolvedByUserId { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string? ResolutionNotes { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public BookingRequest? BookingRequest { get; set; }

        public Lead? ResolvedLead { get; set; }
    }
}
