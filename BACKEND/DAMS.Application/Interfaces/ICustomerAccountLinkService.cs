using DAMS.Application.DTOs.CustomerDtos;

namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// The only code in DAMS permitted to change <c>Customer.UserId</c>.
    ///
    /// <para>
    /// That column is the ownership relationship behind every booking, installment, payment and
    /// receipt a client can read, so writing to it is not a data update — it is a grant of access
    /// to somebody's financial history. Customer deduplication (does this person already have a
    /// CRM record?) and customer ownership (whose login may read it?) are separate questions, and
    /// keeping them in separate services is what stops a matching email or CNIC from quietly
    /// answering the second one.
    /// </para>
    ///
    /// <para>
    /// Exactly three things may establish a link, and each has a method here:
    /// a new Customer created inside the conversion of the client's own booking request; an
    /// existing Customer claimed after an administrator verified the person's identity out of
    /// band; and an explicit administrative correction. Every outcome — including every refusal —
    /// is written to <c>CustomerAccountLinkAudits</c>.
    /// </para>
    /// </summary>
    public interface ICustomerAccountLinkService
    {
        /// <summary>
        /// Links a Customer that was just created during the conversion of a booking request to
        /// the authenticated client who submitted that request.
        ///
        /// <para>
        /// The identity comes from the stored <c>BookingRequest.UserId</c> and from nowhere else —
        /// not from a DTO, not from an email on the request, and not from anything the reviewing
        /// administrator typed. That value was captured from the submitter's own token at the time
        /// they were signed in, which is the only moment DAMS ever had proof of who they were.
        /// </para>
        ///
        /// <para>
        /// Refuses, rather than overwrites, if the Customer already belongs to somebody. Intended
        /// to be called inside the conversion transaction so the link and the booking commit
        /// together.
        /// </para>
        /// </summary>
        Task<CustomerAccountLinkResult> LinkNewCustomerFromBookingRequestAsync(
            int customerId, int bookingRequestId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The claim path: an administrator who has verified out of band that this person is the
        /// customer on an existing record attaches their login to it.
        ///
        /// <para>
        /// A matching email, CNIC, phone or name is not evidence and does not reach this method.
        /// What reaches it is a named administrator, a reason, and the two ids — and all of that
        /// is recorded.
        /// </para>
        /// </summary>
        /// <param name="allowReassign">
        /// Whether to move a Customer that already belongs to a different login. False by default,
        /// so a conflict is refused and surfaced rather than silently resolved; passing true is the
        /// explicit authorized resolution, and is audited as a reassignment.
        /// </param>
        Task<CustomerAccountLinkResult> LinkByAdministratorAsync(
            int customerId, int userId, int administratorUserId, string reason,
            bool allowReassign = false, CancellationToken cancellationToken = default);

        /// <summary>Removes a link, leaving the Customer owned by nobody. Audited like a grant.</summary>
        Task<CustomerAccountLinkResult> UnlinkByAdministratorAsync(
            int customerId, int administratorUserId, string reason, CancellationToken cancellationToken = default);

        /// <summary>Every decision ever taken about who owns this Customer, newest first.</summary>
        Task<IReadOnlyList<CustomerAccountLinkAuditDto>> GetHistoryAsync(
            int customerId, CancellationToken cancellationToken = default);
    }
}
