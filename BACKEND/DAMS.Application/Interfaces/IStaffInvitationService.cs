namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// The single owner of staff activation invitations: it mints the secret, stores only its
    /// hash, and emails the activation link. Account provisioning and the activation endpoint
    /// itself live elsewhere — this service never changes a login's status.
    /// </summary>
    public interface IStaffInvitationService
    {
        /// <summary>
        /// Issues an invitation for an already-Invited login. Any outstanding invitation for
        /// the same login is revoked first, so only the newest token can ever be accepted.
        /// </summary>
        /// <param name="userId">The login being activated.</param>
        /// <param name="invitedByUserId">The authenticated Admin, supplied by the trusted caller.</param>
        Task<StaffInvitationResult> IssueAsync(
            int userId,
            int invitedByUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reissues an invitation. A resend is never a replay: it mints a completely new
        /// token and revokes the previous one rather than extending its expiry.
        /// </summary>
        Task<StaffInvitationResult> ResendAsync(
            int userId,
            int invitedByUserId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Why an invitation could not be issued or delivered. The distinction that matters to
    /// the caller is <see cref="StaffInvitationResult.Issued"/> versus
    /// <see cref="StaffInvitationResult.EmailSent"/>: a persisted invitation with a failed
    /// email is recoverable by a resend, a rejected one is not.
    /// </summary>
    public enum StaffInvitationFailure
    {
        None = 0,

        /// <summary>No such login.</summary>
        UserNotFound = 1,

        /// <summary>The login is Active or Disabled. Status transitions belong to their own
        /// workflow, so this service refuses rather than silently re-inviting.</summary>
        AccountNotInvitable = 2,

        /// <summary>The login has no address to activate.</summary>
        MissingEmail = 3,

        /// <summary>general.publicBaseUrl is unset or unusable, so no trustworthy activation
        /// link can be built. The invitation is still persisted.</summary>
        PublicBaseUrlNotConfigured = 4,

        /// <summary>SMTP rejected or could not deliver the message. The invitation is still
        /// persisted.</summary>
        EmailDeliveryFailed = 5
    }

    /// <summary>
    /// The outcome of issuing an invitation. Deliberately carries nothing secret: no raw
    /// token, no token hash, no credential of any kind.
    /// </summary>
    public sealed class StaffInvitationResult
    {
        /// <summary>The invitation row was committed. A failed email does not undo this.</summary>
        public required bool Issued { get; init; }

        public required bool EmailSent { get; init; }

        public int? InvitationId { get; init; }

        public DateTime? ExpiresAt { get; init; }

        public StaffInvitationFailure Failure { get; init; }

        /// <summary>Safe to show an Admin. Never contains a token or a secret setting.</summary>
        public string? Error { get; init; }

        public static StaffInvitationResult Rejected(StaffInvitationFailure failure, string error) =>
            new() { Issued = false, EmailSent = false, Failure = failure, Error = error };

        public static StaffInvitationResult Delivered(int invitationId, DateTime expiresAt) =>
            new() { Issued = true, EmailSent = true, InvitationId = invitationId, ExpiresAt = expiresAt };

        public static StaffInvitationResult Undelivered(
            int invitationId, DateTime expiresAt, StaffInvitationFailure failure, string error) =>
            new()
            {
                Issued = true,
                EmailSent = false,
                InvitationId = invitationId,
                ExpiresAt = expiresAt,
                Failure = failure,
                Error = error
            };
    }
}
