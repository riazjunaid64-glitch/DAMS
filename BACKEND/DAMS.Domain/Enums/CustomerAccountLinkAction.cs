namespace DAMS.Domain.Enums
{
    /// <summary>
    /// What happened to a <c>Customer.UserId</c> ownership link, or what was refused. Every
    /// value here produces an audit row: a refusal is as much a security event as a grant, and
    /// a conflict nobody looked at is the one that matters most.
    /// </summary>
    public enum CustomerAccountLinkAction
    {
        /// <summary>A new Customer created inside the conversion of the client's own booking
        /// request was linked to the authenticated user who submitted it.</summary>
        LinkedFromBookingRequest = 1,

        /// <summary>An authorized administrator claimed an existing Customer for a client after
        /// verifying their identity out of band.</summary>
        LinkedByAdministrator = 2,

        /// <summary>An authorized administrator moved a Customer from one login to another —
        /// the correction path, and the only way an existing link is ever replaced.</summary>
        ReassignedByAdministrator = 3,

        /// <summary>An authorized administrator removed a link, leaving the Customer unowned.</summary>
        UnlinkedByAdministrator = 4,

        /// <summary>A link was refused because the Customer already belongs to a different
        /// login. Recorded so the conflict is visible rather than silent.</summary>
        RejectedConflict = 5,

        /// <summary>A link was refused because the account was not a verified, active client.</summary>
        RejectedNotEligible = 6
    }
}
