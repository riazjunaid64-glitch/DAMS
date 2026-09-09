using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// See <see cref="ICustomerAccountLinkService"/> for the contract. What lives here is the
    /// single set of rules every ownership decision passes through:
    ///
    /// <list type="bullet">
    /// <item>a login may only own customer data if it is an Active client whose email address has
    /// actually been verified — an unverified account is somebody who typed an address, not
    /// somebody who holds it;</item>
    /// <item>an existing link is never replaced as a side effect of anything, only by an
    /// administrator who asked for a reassignment in so many words;</item>
    /// <item>every outcome, grant and refusal alike, leaves an audit row.</item>
    /// </list>
    ///
    /// <para>
    /// Deliberately absent: any lookup by email, CNIC, phone or name. This service is never given
    /// the chance to decide that two records describe the same person — that is deduplication, it
    /// lives in <see cref="CustomerService"/>, and it is not evidence of anything.
    /// </para>
    /// </summary>
    public sealed class CustomerAccountLinkService : ICustomerAccountLinkService
    {
        private readonly AppDbContext _context;
        private readonly TimeProvider _clock;

        public CustomerAccountLinkService(AppDbContext context, TimeProvider clock)
        {
            _context = context;
            _clock = clock;
        }

        public async Task<CustomerAccountLinkResult> LinkNewCustomerFromBookingRequestAsync(
            int customerId, int bookingRequestId, CancellationToken cancellationToken = default)
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
                return NotFound(customerId, CustomerAccountLinkOutcome.CustomerNotFound, "Customer not found.");

            var request = await _context.BookingRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(br => br.Id == bookingRequestId, cancellationToken);

            // A website request submitted while signed out carries no identity, so there is
            // nothing to preserve and nothing to grant. The booking still goes through; what does
            // not happen is a stranger inheriting the customer record.
            if (request?.UserId == null)
                return new CustomerAccountLinkResult(
                    CustomerAccountLinkOutcome.BookingRequestNotEligible, customerId, customer.UserId,
                    "The booking request carries no authenticated submitter, so no account was linked.");

            var userId = request.UserId.Value;

            if (customer.UserId == userId)
                return new CustomerAccountLinkResult(
                    CustomerAccountLinkOutcome.AlreadyLinked, customerId, userId,
                    "This customer already belongs to that account.");

            // The caller said this Customer was created for this request. If it already belongs to
            // somebody, one of those two things is untrue — either way, nothing is overwritten.
            if (customer.UserId != null)
                return await RecordAsync(customer, CustomerAccountLinkAction.RejectedConflict,
                    attemptedUserId: userId, performedByUserId: null, bookingRequestId: bookingRequestId,
                    reason: "Conversion tried to link a customer that already belongs to another account.",
                    outcome: CustomerAccountLinkOutcome.Conflict,
                    message: "This customer is already linked to a different account. "
                        + "Resolve the conflict before linking it to this one.",
                    cancellationToken: cancellationToken);

            if (!await IsEligibleClientAsync(userId, cancellationToken))
                return await RecordAsync(customer, CustomerAccountLinkAction.RejectedNotEligible,
                    attemptedUserId: userId, performedByUserId: null, bookingRequestId: bookingRequestId,
                    reason: "The submitting account is not a verified active client.",
                    outcome: CustomerAccountLinkOutcome.AccountNotEligible,
                    message: "That account is not a verified client account, so no link was made.",
                    cancellationToken: cancellationToken);

            customer.UserId = userId;
            customer.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

            return await RecordAsync(customer, CustomerAccountLinkAction.LinkedFromBookingRequest,
                attemptedUserId: userId, performedByUserId: null, bookingRequestId: bookingRequestId,
                reason: $"New customer created from booking request #{bookingRequestId}.",
                outcome: CustomerAccountLinkOutcome.Linked,
                message: "Customer linked to the account that submitted the request.",
                cancellationToken: cancellationToken);
        }

        public async Task<CustomerAccountLinkResult> LinkByAdministratorAsync(
            int customerId, int userId, int administratorUserId, string reason,
            bool allowReassign = false, CancellationToken cancellationToken = default)
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
                return NotFound(customerId, CustomerAccountLinkOutcome.CustomerNotFound, "Customer not found.");

            if (!await _context.Users.AnyAsync(u => u.UserId == userId, cancellationToken))
                return NotFound(customerId, CustomerAccountLinkOutcome.UserNotFound, "That login does not exist.");

            if (customer.UserId == userId)
                return new CustomerAccountLinkResult(
                    CustomerAccountLinkOutcome.AlreadyLinked, customerId, userId,
                    "This customer already belongs to that account.");

            if (!await IsEligibleClientAsync(userId, cancellationToken))
                return await RecordAsync(customer, CustomerAccountLinkAction.RejectedNotEligible,
                    attemptedUserId: userId, performedByUserId: administratorUserId, bookingRequestId: null,
                    reason: reason,
                    outcome: CustomerAccountLinkOutcome.AccountNotEligible,
                    message: "That login is not an active client account with a verified email address. "
                        + "It cannot be given access to customer records.",
                    cancellationToken: cancellationToken);

            var previousUserId = customer.UserId;

            // Taking a customer away from the login that currently owns it removes somebody's
            // access to their own bookings. It happens only when the administrator asked for
            // exactly that.
            if (previousUserId != null && !allowReassign)
                return await RecordAsync(customer, CustomerAccountLinkAction.RejectedConflict,
                    attemptedUserId: userId, performedByUserId: administratorUserId, bookingRequestId: null,
                    reason: reason,
                    outcome: CustomerAccountLinkOutcome.Conflict,
                    message: "This customer is already linked to a different account. Confirm the "
                        + "reassignment explicitly if the existing link is wrong.",
                    cancellationToken: cancellationToken);

            customer.UserId = userId;
            customer.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

            return previousUserId == null
                ? await RecordAsync(customer, CustomerAccountLinkAction.LinkedByAdministrator,
                    attemptedUserId: userId, performedByUserId: administratorUserId, bookingRequestId: null,
                    reason: reason,
                    outcome: CustomerAccountLinkOutcome.Linked,
                    message: "Customer linked to the account.",
                    cancellationToken: cancellationToken)
                : await RecordAsync(customer, CustomerAccountLinkAction.ReassignedByAdministrator,
                    attemptedUserId: userId, performedByUserId: administratorUserId, bookingRequestId: null,
                    reason: reason, previousUserId: previousUserId,
                    outcome: CustomerAccountLinkOutcome.Reassigned,
                    message: "Customer moved to the account.",
                    cancellationToken: cancellationToken);
        }

        public async Task<CustomerAccountLinkResult> UnlinkByAdministratorAsync(
            int customerId, int administratorUserId, string reason, CancellationToken cancellationToken = default)
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
                return NotFound(customerId, CustomerAccountLinkOutcome.CustomerNotFound, "Customer not found.");

            var previousUserId = customer.UserId;
            if (previousUserId == null)
                return new CustomerAccountLinkResult(
                    CustomerAccountLinkOutcome.Unlinked, customerId, null,
                    "This customer is not linked to any account.");

            customer.UserId = null;
            customer.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

            return await RecordAsync(customer, CustomerAccountLinkAction.UnlinkedByAdministrator,
                attemptedUserId: previousUserId, performedByUserId: administratorUserId, bookingRequestId: null,
                reason: reason, previousUserId: previousUserId,
                outcome: CustomerAccountLinkOutcome.Unlinked,
                message: "Customer unlinked. Its bookings are no longer visible to that account.",
                cancellationToken: cancellationToken);
        }

        public async Task<IReadOnlyList<CustomerAccountLinkAuditDto>> GetHistoryAsync(
            int customerId, CancellationToken cancellationToken = default)
        {
            return await _context.CustomerAccountLinkAudits
                .AsNoTracking()
                .Where(a => a.CustomerId == customerId)
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Select(a => new CustomerAccountLinkAuditDto
                {
                    Id = a.Id,
                    CustomerId = a.CustomerId,
                    PreviousUserId = a.PreviousUserId,
                    AttemptedUserId = a.AttemptedUserId,
                    ResultingUserId = a.ResultingUserId,
                    Action = a.Action,
                    PerformedByUserId = a.PerformedByUserId,
                    PerformedByName = _context.Users
                        .Where(u => u.UserId == a.PerformedByUserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),
                    BookingRequestId = a.BookingRequestId,
                    Reason = a.Reason,
                    OccurredAt = a.OccurredAt
                })
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Whether a login may own customer financial data at all: an Active account, in the
        /// Client role, whose email address DAMS has actually seen proven. The verification stamp
        /// is checked separately from the status because a legacy account can be Active for
        /// reasons that predate verification entirely.
        /// </summary>
        private async Task<bool> IsEligibleClientAsync(int userId, CancellationToken cancellationToken) =>
            await _context.Users
                .Include(u => u.Role)
                .AnyAsync(u => u.UserId == userId
                            && u.AccountStatus == UserAccountStatus.Active
                            && u.EmailVerifiedAt != null
                            && u.Role.Role_name == "Client",
                    cancellationToken);

        /// <summary>
        /// Writes the audit row and saves. The link change and its audit row go in one
        /// SaveChanges, so a stored link with no record of how it got there is not a state DAMS
        /// can reach. Inside a caller's transaction this simply joins it.
        /// </summary>
        private async Task<CustomerAccountLinkResult> RecordAsync(
            Customer customer,
            CustomerAccountLinkAction action,
            int? attemptedUserId,
            int? performedByUserId,
            int? bookingRequestId,
            string? reason,
            CustomerAccountLinkOutcome outcome,
            string message,
            CancellationToken cancellationToken,
            int? previousUserId = null)
        {
            _context.CustomerAccountLinkAudits.Add(new CustomerAccountLinkAudit
            {
                CustomerId = customer.Id,
                // On a refusal nothing moved, so "before" is simply what is still there.
                PreviousUserId = previousUserId
                    ?? (action is CustomerAccountLinkAction.RejectedConflict ? customer.UserId : null),
                AttemptedUserId = attemptedUserId,
                ResultingUserId = customer.UserId,
                Action = action,
                PerformedByUserId = performedByUserId,
                BookingRequestId = bookingRequestId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                OccurredAt = _clock.GetUtcNow().UtcDateTime
            });

            await _context.SaveChangesAsync(cancellationToken);

            return new CustomerAccountLinkResult(outcome, customer.Id, customer.UserId, message);
        }

        private static CustomerAccountLinkResult NotFound(
            int customerId, CustomerAccountLinkOutcome outcome, string message) =>
            new(outcome, customerId, null, message);
    }
}
