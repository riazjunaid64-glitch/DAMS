namespace DAMS.Application.Interfaces
{
    /// <summary>
    /// Owns the whole life of a client email-verification credential: minting one, emailing it,
    /// and spending it. Nothing else in DAMS hashes, looks up, expires or consumes one — a second
    /// implementation of "is this token good?" is precisely the bug this service exists to stop.
    ///
    /// <para>
    /// Every method answers an anonymous caller, so none of them ever says whether an address is
    /// registered, what state an account is in, or which of the many reasons a token failed.
    /// </para>
    /// </summary>
    public interface IClientEmailVerificationService
    {
        /// <summary>
        /// Issues a fresh verification credential for a pending client login and emails the link,
        /// revoking any credential still outstanding for that login. Safe to call repeatedly; the
        /// caller learns nothing about the account from the result.
        /// </summary>
        Task<ClientVerificationIssueResult> IssueAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Issues for whichever pending client login owns this email address, if any. Does nothing
        /// at all when the address is unknown or its account is not awaiting verification — and
        /// reports the same thing either way.
        /// </summary>
        Task ResendAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>
        /// Spends a token: records the verification, stores the password the mailbox owner chose,
        /// and activates the account — as one commit, so there is never an instant where the
        /// account works and the credential still does too.
        ///
        /// <para>
        /// Unknown, expired, already-spent, superseded and revoked tokens are all one answer.
        /// Only the two things the person can actually fix — a password that is too short or too
        /// long for bcrypt — come back as themselves.
        /// </para>
        /// </summary>
        Task<ClientVerificationResult> VerifyAsync(
            string rawToken, string chosenPassword, CancellationToken cancellationToken = default);
    }

    /// <summary>Why an issuance did not put an email in somebody's inbox. Never surfaced to the
    /// anonymous caller who triggered it — this is for logs, tests and Admin-facing paths.</summary>
    public enum ClientVerificationIssueFailure
    {
        None = 0,
        AccountNotPending,
        MissingEmail,
        PublicBaseUrlNotConfigured,
        EmailDeliveryFailed
    }

    public sealed record ClientVerificationIssueResult(
        bool Issued, bool Delivered, ClientVerificationIssueFailure Failure = ClientVerificationIssueFailure.None)
    {
        public static ClientVerificationIssueResult Sent() => new(true, true);

        /// <summary>Stored but not delivered. The credential stands and a resend can try again.</summary>
        public static ClientVerificationIssueResult Undelivered(ClientVerificationIssueFailure failure) =>
            new(true, false, failure);

        public static ClientVerificationIssueResult NotIssued(ClientVerificationIssueFailure failure) =>
            new(false, false, failure);
    }

    public enum ClientVerificationFailure
    {
        None = 0,

        /// <summary>Unknown, expired, spent, superseded, revoked, or an account that may no longer
        /// be verified. Deliberately one value: telling them apart tells an attacker which
        /// addresses are registered and which links have already been used.</summary>
        InvalidVerification,

        PasswordTooShort,
        PasswordTooLong
    }

    public sealed record ClientVerificationResult(bool Verified, ClientVerificationFailure Failure, string? Error)
    {
        public static ClientVerificationResult Success() => new(true, ClientVerificationFailure.None, null);

        public static ClientVerificationResult Invalid() => new(
            false, ClientVerificationFailure.InvalidVerification,
            "This verification link is invalid or has expired. Request a new one and try again.");

        public static ClientVerificationResult Rejected(ClientVerificationFailure failure, string error) =>
            new(false, failure, error);
    }
}
