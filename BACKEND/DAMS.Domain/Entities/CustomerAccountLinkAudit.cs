using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The permanent record of every attempt to decide who owns a Customer record.
    ///
    /// <para>
    /// <c>Customer.UserId</c> is the ownership relationship behind every booking, installment,
    /// payment and receipt a client can see, so a change to it is a change to who can read
    /// somebody's financial history. Rows are written for grants, reassignments, removals and
    /// refusals alike, and are never updated or deleted — a link whose history cannot be
    /// reconstructed is indistinguishable from one that was quietly taken.
    /// </para>
    /// </summary>
    public class CustomerAccountLinkAudit
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        /// <summary>The login the customer was owned by before this decision, if any.</summary>
        public int? PreviousUserId { get; set; }

        /// <summary>The login the decision concerned — the one granted, refused or removed.</summary>
        public int? AttemptedUserId { get; set; }

        /// <summary>The login the customer is owned by after this decision, if any.</summary>
        public int? ResultingUserId { get; set; }

        public CustomerAccountLinkAction Action { get; set; }

        /// <summary>Who performed it. Null for a system conversion acting on its own trusted path.</summary>
        public int? PerformedByUserId { get; set; }

        /// <summary>The booking request whose stored identity justified the link, when that is
        /// what justified it.</summary>
        public int? BookingRequestId { get; set; }

        /// <summary>Why. Required from an administrator; supplied by the workflow otherwise.</summary>
        public string? Reason { get; set; }

        public DateTime OccurredAt { get; set; }

        public Customer Customer { get; set; } = null!;
    }
}
