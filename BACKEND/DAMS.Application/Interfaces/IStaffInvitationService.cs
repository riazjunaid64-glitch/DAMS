namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// The single owner of staff activation invitations: it mints the secret, stores only its
    /// hash, emails the activation link, and is the only thing that can spend one. Nothing
    /// outside this service hashes a token, looks one up, or judges whether it is still good.
    /// <para>
    /// Issuing never changes a login's status. Activation does, and only in one direction: the
    /// Invited login an accepted token belongs to becomes Active as part of the same write that
    /// consumes the invitation.
    /// </para>
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

        /// <summary>
        /// Spends an activation token: the employee's own password is hashed onto the login,
        /// the login becomes Active, and the invitation is consumed so the link cannot work a
        /// second time. All of that is one atomic database operation.
        /// <para>
        /// Anonymous callers reach this, so the token alone decides which account is being
        /// activated — there is no identifier to supply and none is accepted. Every reason a
        /// token might not work returns the same answer, because telling an unauthenticated
        /// caller which reason applies would tell them which accounts exist.
        /// </para>
        /// </summary>
        /// <param name="rawToken">The secret from the emailed activation link.</param>
        /// <param name="chosenPassword">The employee's own password. Never seen by an Admin,
        /// never stored except as a bcrypt hash.</param>
        Task<StaffActivationResult> ActivateAsync(
            string rawToken,
            string chosenPassword,
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

    /// <summary>
    /// Why an activation was refused. Only the password reasons are specific: they describe
    /// what the caller typed, which they already know. Every reason that describes the
    /// invitation or the account behind it collapses into
    /// <see cref="InvalidInvitation"/> so an anonymous caller learns nothing from the answer.
    /// </summary>
    public enum StaffActivationFailure
    {
        None = 0,

        /// <summary>Unknown, expired, revoked or already-used token; a login that is no longer
        /// Invited or no longer there; an employee who is missing or no longer active. One
        /// value on purpose — the differences are not the caller's business.</summary>
        InvalidInvitation = 1,

        PasswordTooShort = 2,

        PasswordTooLong = 3
    }

    /// <summary>
    /// The outcome of spending an activation token. It carries no session, no token and no
    /// account detail: activation proves the employee owns the mailbox, which is not the same
    /// event as signing in, so a successful activation hands back nothing but the news.
    /// </summary>
    public sealed class StaffActivationResult
    {
        /// <summary>The password was set and the login is now Active.</summary>
        public required bool Activated { get; init; }

        public StaffActivationFailure Failure { get; init; }

        /// <summary>Safe to show anyone. Never says which account, or whether one exists.</summary>
        public string? Error { get; init; }

        /// <summary>The single answer to every bad link, so the wording itself cannot be used
        /// to tell one failure apart from another.</summary>
        public const string InvalidInvitationMessage =
            "This activation link is invalid or has expired. Ask your administrator for a new invitation.";

        public static StaffActivationResult Success() =>
            new() { Activated = true };

        public static StaffActivationResult InvalidInvitation() =>
            new()
            {
                Activated = false,
                Failure = StaffActivationFailure.InvalidInvitation,
                Error = InvalidInvitationMessage
            };

        public static StaffActivationResult Rejected(StaffActivationFailure failure, string error) =>
            new() { Activated = false, Failure = failure, Error = error };
    }
}
