using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    /// <summary>Why a link attempt ended the way it did. One value per outcome the caller can act on.</summary>
    public enum CustomerAccountLinkOutcome
    {
        /// <summary>The Customer now belongs to the requested login.</summary>
        Linked = 0,

        /// <summary>It already did. Nothing changed, and nothing was wrong.</summary>
        AlreadyLinked,

        /// <summary>The link was moved from one login to another under explicit authorization.</summary>
        Reassigned,

        /// <summary>The link was removed.</summary>
        Unlinked,

        /// <summary>
        /// The Customer belongs to a different login. Never resolved automatically — an
        /// administrator has to decide, and say so.
        /// </summary>
        Conflict,

        /// <summary>The login is not a verified, active client, so it cannot own customer data.</summary>
        AccountNotEligible,

        CustomerNotFound,

        UserNotFound,

        /// <summary>The booking request does not exist, carries no authenticated submitter, or is
        /// not the one this customer was created from.</summary>
        BookingRequestNotEligible
    }

    public sealed record CustomerAccountLinkResult(
        CustomerAccountLinkOutcome Outcome,
        int CustomerId,
        int? LinkedUserId,
        string Message)
    {
        /// <summary>Whether the Customer ended up owned by the login the caller asked about.</summary>
        public bool Succeeded => Outcome is CustomerAccountLinkOutcome.Linked
            or CustomerAccountLinkOutcome.AlreadyLinked
            or CustomerAccountLinkOutcome.Reassigned;
    }

    public sealed class CustomerAccountLinkAuditDto
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? PreviousUserId { get; set; }
        public int? AttemptedUserId { get; set; }
        public int? ResultingUserId { get; set; }
        public CustomerAccountLinkAction Action { get; set; }
        public int? PerformedByUserId { get; set; }
        public string? PerformedByName { get; set; }
        public int? BookingRequestId { get; set; }
        public string? Reason { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    /// <summary>
    /// What an administrator sends to claim an existing Customer for a client's login. There is
    /// deliberately no email, CNIC or phone field: those are how the wrong person gets in, and
    /// nothing the caller could type here would be believed anyway.
    /// </summary>
    public sealed class LinkCustomerAccountDto
    {
        [Required]
        public int UserId { get; set; }

        /// <summary>
        /// How the person's identity was established. Required, and stored — the audit trail is
        /// the only thing that makes an out-of-band verification reviewable afterwards.
        /// </summary>
        [Required]
        [StringLength(500, MinimumLength = 10)]
        public string Reason { get; set; } = null!;

        /// <summary>
        /// Set only to move a Customer that already belongs to a different login. Without it a
        /// conflict is refused, which is the default on purpose.
        /// </summary>
        public bool AllowReassign { get; set; }
    }

    public sealed class UnlinkCustomerAccountDto
    {
        [Required]
        [StringLength(500, MinimumLength = 10)]
        public string Reason { get; set; } = null!;
    }
}
