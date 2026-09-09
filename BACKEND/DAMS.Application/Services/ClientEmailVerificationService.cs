using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Domain.Identity;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Proves that whoever registered an email address can actually read that mailbox, and only
    /// then lets an account exist in a usable form.
    ///
    /// <para>
    /// The order matters more than any single check in it. Registration does not set a password;
    /// verification does. Otherwise a stranger could register a buyer's address, choose a
    /// password, and still hold that password afterwards — even once the real buyer had clicked
    /// the link that arrived unexpectedly in their inbox. The person who demonstrably holds the
    /// mailbox is the person who establishes the credential.
    /// </para>
    ///
    /// <para>
    /// Modelled on <see cref="StaffInvitationService"/> and sharing its security properties —
    /// 256-bit token, hash at rest, single use, serialisable redemption, and no logger anywhere
    /// on the path so no code can write a token to a log — but kept as its own service and its
    /// own table because the two workflows differ in who vouches for the person, what may open
    /// an account, and what that account reaches afterwards.
    /// </para>
    /// </summary>
    public sealed class ClientEmailVerificationService : IClientEmailVerificationService
    {
        /// <summary>A verification link is a credential, so it expires quickly.</summary>
        public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

        /// <summary>The frontend route that trades the token for a chosen password.</summary>
        public const string VerificationPath = "/verify-email";

        /// <summary>
        /// The token travels in the URL fragment, never the query string — for the same reason
        /// staff activation does. A fragment is not part of the HTTP request, so no web server,
        /// proxy or CDN in front of DAMS can write the credential to an access log before any of
        /// the page's own scrubbing has had a chance to run.
        /// </summary>
        public const string TokenParameter = "token";

        /// <summary>The floor DAMS applies to every self-chosen password.</summary>
        public const int MinPasswordLength = 8;

        /// <summary>
        /// bcrypt hashes only the first 72 bytes of its input. Two longer passwords sharing a
        /// 72-byte prefix would open the same account, so DAMS refuses rather than quietly
        /// shortening one the customer believes is longer.
        /// </summary>
        public const int MaxPasswordBytes = 72;

        /// <summary>256 bits of randomness — the token is guessed, never derived from an ID.</summary>
        private const int TokenBytes = 32;

        /// <summary>An issued token is 43 characters. Anything far longer is not a near miss, so
        /// it is dropped before it costs a hash or a query.</summary>
        private const int MaxTokenLength = 200;

        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly IEmailSender _email;
        private readonly TimeProvider _clock;

        public ClientEmailVerificationService(
            AppDbContext context,
            NotificationSettingsStore settings,
            IEmailSender email,
            TimeProvider clock)
        {
            _context = context;
            _settings = settings;
            _email = email;
            _clock = clock;
        }

        public Task<ClientVerificationIssueResult> IssueAsync(int userId, CancellationToken cancellationToken = default) =>
            IssueInternalAsync(userId, cancellationToken);

        public async Task ResendAsync(string email, CancellationToken cancellationToken = default)
        {
            var normalized = EmailIdentity.Normalize(email);
            if (normalized == null)
                return;

            // Only an account actually waiting on verification is resent to. An Active, Invited or
            // Disabled login gets nothing — and the endpoint above answers identically either way,
            // so this is not a way to discover which addresses are registered.
            var userId = await _context.Users
                .Where(u => u.NormalizedEmail == normalized
                            && u.AccountStatus == UserAccountStatus.PendingEmailVerification)
                .Select(u => (int?)u.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (userId.HasValue)
                await IssueInternalAsync(userId.Value, cancellationToken);
        }

        /// <summary>
        /// Spends a token. Shape of the work, as in staff activation: reject cheaply, hash outside
        /// the transaction, then re-check and commit everything as one write.
        /// </summary>
        public async Task<ClientVerificationResult> VerifyAsync(
            string rawToken, string chosenPassword, CancellationToken cancellationToken = default)
        {
            // Nothing here touches the database or the CPU-expensive hash, so junk input costs a
            // public endpoint almost nothing.
            if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > MaxTokenLength)
                return ClientVerificationResult.Invalid();

            var passwordProblem = ValidatePassword(chosenPassword);
            if (passwordProblem != null)
                return passwordProblem;

            var tokenHash = HashToken(rawToken);
            var now = _clock.GetUtcNow().UtcDateTime;

            // A first look outside the transaction: a guessed or stale token is turned away by one
            // indexed read, without paying for a bcrypt hash or a serialisable lock. It decides
            // nothing — the check that counts is the one inside the transaction.
            if (await FindUsableAsync(tokenHash, now, cancellationToken) == null)
                return ClientVerificationResult.Invalid();

            // bcrypt is slow on purpose and depends on nothing the transaction reads, so it runs
            // before the transaction opens rather than while holding locks.
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(chosenPassword);

            var result = ClientVerificationResult.Invalid();
            await ExecuteResilientlyAsync(async () =>
            {
                // The strategy can re-run this whole block after a deadlock, so it has to start
                // from what is stored rather than from anything a previous attempt tracked.
                _context.ChangeTracker.Clear();
                await using var transaction = await BeginSerialisableAsync(cancellationToken);

                // Read the clock again rather than reusing the one from the first look: a retry
                // must not judge expiry against a time that has since passed.
                var attemptedAt = _clock.GetUtcNow().UtcDateTime;

                // Re-read under the transaction. This is where single use is actually enforced:
                // the loser of a concurrent redemption either waits for the winner's commit and
                // then sees a spent credential, or is picked as the deadlock victim and retries
                // into exactly the same outcome.
                var verification = await FindUsableAsync(tokenHash, attemptedAt, cancellationToken);
                if (verification == null)
                {
                    result = ClientVerificationResult.Invalid();
                    return;
                }

                var user = verification.User;

                // The mailbox owner's password — the first this account has ever had, or a fresh
                // one replacing whatever a legacy account carried before verification existed.
                user.Password = passwordHash;
                user.EmailVerifiedAt = attemptedAt;
                user.AccountStatus = UserAccountStatus.Active;

                // A pending login should have no session at all. Clearing anyway means a legacy or
                // imported row cannot carry an old session into a freshly verified account — which
                // is exactly how a pre-migration client session would otherwise survive.
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;

                verification.VerifiedAt = attemptedAt;

                // Any link still outstanding dies with this one, so an older email in the same
                // inbox cannot be replayed later to change the password again.
                var superseded = await _context.ClientEmailVerifications
                    .Where(v => v.UserId == user.UserId && v.Id != verification.Id
                        && v.VerifiedAt == null && v.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var stale in superseded)
                    stale.RevokedAt = attemptedAt;

                // One SaveChanges for the password, the verification stamp, the status and the
                // consumed credential: there is no instant where the account is usable and the
                // link still is too.
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                result = ClientVerificationResult.Success();
            });

            return result;
        }

        /// <summary>
        /// The verification a token opens, or null for every reason it might not: unknown,
        /// expired, revoked, already spent, or a login that is no longer a client awaiting
        /// verification. One return value for all of them, because the caller must not be able to
        /// tell them apart.
        /// </summary>
        private async Task<ClientEmailVerification?> FindUsableAsync(
            string tokenHash, DateTime now, CancellationToken cancellationToken)
        {
            var verification = await _context.ClientEmailVerifications
                .Include(v => v.User).ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(v => v.TokenHash == tokenHash, cancellationToken);

            if (verification == null
                || verification.VerifiedAt != null
                || verification.RevokedAt != null
                || verification.ExpiresAt <= now)
                return null;

            var user = verification.User;
            if (user == null || user.AccountStatus != UserAccountStatus.PendingEmailVerification)
                return null;

            // Client verification opens a client account and nothing else. A login that has since
            // been given a staff role goes through staff activation, which applies employment
            // checks this workflow knows nothing about.
            return IsClient(user) ? verification : null;
        }

        private static bool IsClient(User user) =>
            string.Equals(user.Role?.Role_name, "Client", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Serialisable, because both halves of this service are read-then-write over the same
        /// set: "read it as unused, then write it as used" for redemption, and "read the
        /// outstanding ones, then revoke and insert" for issuance. Neither is single-use unless
        /// nothing can slip between the two steps. Skipped on a non-relational provider, which has
        /// no concurrency to protect against, and skipped inside a caller's transaction rather
        /// than nesting one.
        /// </summary>
        private async Task<IDbContextTransaction?> BeginSerialisableAsync(CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
                return null;

            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        /// <summary>
        /// Runs the work as one retriable unit. Not optional: the API configures SQL Server with
        /// EnableRetryOnFailure, and EF refuses to run anything inside a caller-opened transaction
        /// unless it goes through the execution strategy.
        /// </summary>
        private Task ExecuteResilientlyAsync(Func<Task> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

        /// <summary>The password rules, as the two things the customer can actually fix.</summary>
        private static ClientVerificationResult? ValidatePassword(string? chosenPassword)
        {
            if (string.IsNullOrEmpty(chosenPassword) || chosenPassword.Length < MinPasswordLength)
                return ClientVerificationResult.Rejected(
                    ClientVerificationFailure.PasswordTooShort,
                    $"Choose a password of at least {MinPasswordLength} characters.");

            // Deliberately no number in this message. The limit is 72 *bytes*, and "72 characters"
            // would be a lie to anybody typing accented letters or emoji.
            if (Encoding.UTF8.GetByteCount(chosenPassword) > MaxPasswordBytes)
                return ClientVerificationResult.Rejected(
                    ClientVerificationFailure.PasswordTooLong,
                    "That password is too long. Shorten it and try again.");

            return null;
        }

        /// <summary>
        /// The lookup form of a token: the raw value never has to be stored to be verified.
        /// Private on purpose — see the class summary.
        /// </summary>
        private static string HashToken(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        /// <summary>
        /// What the transactional half of issuance produced, so the mailing half can run with no
        /// database work left to do. The raw token lives here and nowhere else.
        /// </summary>
        private sealed record MintedVerification(DateTime ExpiresAt, string RawToken, string Email, string FullName);

        private async Task<ClientVerificationIssueResult> IssueInternalAsync(
            int userId, CancellationToken cancellationToken)
        {
            // Split deliberately. Everything that decides and writes happens in the retriable
            // serialisable block below; the mail is sent afterwards, outside both the transaction
            // and the retry, so a deadlock retry can never send a second copy of a link.
            ClientVerificationIssueResult? rejected = null;
            MintedVerification? minted = null;

            await ExecuteResilientlyAsync(async () =>
            {
                // A retry re-runs this whole block, so it has to start from what is stored.
                _context.ChangeTracker.Clear();
                rejected = null;
                minted = null;

                await using var transaction = await BeginSerialisableAsync(cancellationToken);

                var user = await _context.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

                // Read inside the transaction: a concurrent verification that commits first must
                // be seen here, or this would mint a link for an account that is already Active.
                if (user == null
                    || user.AccountStatus != UserAccountStatus.PendingEmailVerification
                    || !IsClient(user))
                {
                    rejected = ClientVerificationIssueResult.NotIssued(
                        ClientVerificationIssueFailure.AccountNotPending);
                    return;
                }

                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    rejected = ClientVerificationIssueResult.NotIssued(
                        ClientVerificationIssueFailure.MissingEmail);
                    return;
                }

                var now = _clock.GetUtcNow().UtcDateTime;
                var rawToken = GenerateToken();

                // Read-then-write over the outstanding set, which is only "at most one usable
                // credential" if nothing can insert between the read and the write — hence the
                // serialisable transaction rather than SaveChanges' own implicit one.
                var outstanding = await _context.ClientEmailVerifications
                    .Where(v => v.UserId == userId && v.VerifiedAt == null && v.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var superseded in outstanding)
                    superseded.RevokedAt = now;

                _context.ClientEmailVerifications.Add(new ClientEmailVerification
                {
                    UserId = user.UserId,
                    TokenHash = HashToken(rawToken),
                    CreatedAt = now,
                    ExpiresAt = now.Add(VerificationLifetime)
                });

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                minted = new MintedVerification(
                    now.Add(VerificationLifetime), rawToken, user.Email, user.FullName);
            });

            if (rejected != null)
                return rejected;

            // Committed. Everything below is best-effort delivery: the credential stays stored
            // either way, the account is not verified, no password exists, and a resend can retry.
            var issued = minted!;
            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var verificationUrl = BuildVerificationUrl(branding.PublicBaseUrl, issued.RawToken);
            if (verificationUrl == null)
                return ClientVerificationIssueResult.Undelivered(
                    ClientVerificationIssueFailure.PublicBaseUrlNotConfigured);

            var send = await _email.SendAsync(
                BuildEmail(issued.Email, issued.FullName, branding, verificationUrl), cancellationToken);

            return send.Success
                ? ClientVerificationIssueResult.Sent()
                : ClientVerificationIssueResult.Undelivered(ClientVerificationIssueFailure.EmailDeliveryFailed);
        }

        /// <summary>URL-safe base64 over 32 cryptographically random bytes.</summary>
        private static string GenerateToken()
        {
            var bytes = new byte[TokenBytes];
            RandomNumberGenerator.Fill(bytes);

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// The link origin comes only from the Admin-configured public base URL — never from a
        /// request Host, Origin or Referer header, which the browser controls. Returns null when
        /// nothing trustworthy is configured, so a broken link is never emailed.
        /// </summary>
        private static string? BuildVerificationUrl(string? baseUrl, string rawToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var origin)
                || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
                return null;

            return $"{origin.GetLeftPart(UriPartial.Authority)}{VerificationPath}"
                + $"#{TokenParameter}={Uri.EscapeDataString(rawToken)}";
        }

        /// <summary>
        /// A deliberately plain security email: an identity, a reason, one link, an expiry and a
        /// way to raise the alarm. No password, no credential and no internal ID.
        ///
        /// <para>
        /// The "if you did not sign up" line is not boilerplate here. This message is the one
        /// thing that reaches somebody whose address a stranger tried to register with, and it
        /// has to be able to say that ignoring it is enough — which is only true because
        /// registration never set a password.
        /// </para>
        /// </summary>
        private static EmailMessage BuildEmail(
            string recipientEmail, string recipientName, NotificationBranding branding, string verificationUrl)
        {
            var company = branding.CompanyName;
            var app = branding.AppName;
            var name = string.IsNullOrWhiteSpace(recipientName) ? "there" : recipientName;
            var hours = (int)VerificationLifetime.TotalHours;
            var support = branding.SupportEmail;

            var safeName = WebUtility.HtmlEncode(name);
            var safeCompany = WebUtility.HtmlEncode(company);
            var safeApp = WebUtility.HtmlEncode(app);
            var safeUrl = WebUtility.HtmlEncode(verificationUrl);
            var safeSupport = support == null ? null : WebUtility.HtmlEncode(support);

            var html = new StringBuilder()
                .Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>Confirm your ").Append(safeApp).Append(" account</title></head>")
                .Append("<body style=\"margin:0;padding:0;background:#f4f5f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2933;\">")
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f5f7;padding:24px 12px;\"><tr><td align=\"center\">")
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;\">")
                .Append("<tr><td style=\"background:").Append(branding.PrimaryColor).Append(";padding:20px 24px;\">")
                .Append("<span style=\"color:#ffffff;font-size:18px;font-weight:600;\">").Append(safeCompany).Append("</span>")
                .Append("</td></tr>")
                .Append("<tr><td style=\"padding:24px;font-size:15px;line-height:1.6;color:#334e68;\">")
                .Append("<h1 style=\"margin:0 0 12px;font-size:20px;line-height:1.3;color:#102a43;\">Confirm your ")
                .Append(safeApp).Append(" account</h1>")
                .Append("<p style=\"margin:0 0 12px;\">Hello ").Append(safeName).Append(",</p>")
                .Append("<p style=\"margin:0 0 12px;\">Somebody used this email address to create a ").Append(safeApp)
                .Append(" account with ").Append(safeCompany)
                .Append(". If that was you, confirm the address and choose your password using the link below. ")
                .Append("The account cannot be used until you do, and nobody at ").Append(safeCompany)
                .Append(" knows or can see your password.</p>")
                .Append("<div style=\"margin:24px 0 4px;\"><a href=\"").Append(safeUrl)
                .Append("\" style=\"display:inline-block;background:").Append(branding.AccentColor)
                .Append(";color:#1a1a1a;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:8px;font-size:15px;\">")
                .Append("Confirm my email address</a></div>")
                .Append("<p style=\"font-size:12px;color:#829ab1;margin:12px 0 0;word-break:break-all;\">")
                .Append("If the button does not work, open: ").Append(safeUrl).Append("</p>")
                .Append("<p style=\"margin:20px 0 0;\">This link expires in ").Append(hours)
                .Append(" hours and can be used once. If it has expired, request a new one from the sign-in page.</p>")
                .Append("<p style=\"margin:12px 0 0;color:#52606d;\">If you did not sign up, no account has been opened in your name and no password exists. ")
                .Append("You can safely ignore this email");
            if (safeSupport != null)
                html.Append(", or tell us at <a href=\"mailto:").Append(safeSupport)
                    .Append("\" style=\"color:#334e68;\">").Append(safeSupport).Append("</a>");
            html.Append(".</p>")
                .Append("</td></tr>")
                .Append("<tr><td style=\"background:#f8f9fb;padding:18px 24px;font-size:12px;color:#627d98;border-top:1px solid #e4e7eb;\">")
                .Append(safeCompany)
                .Append("</td></tr></table></td></tr></table></body></html>");

            var text = new StringBuilder()
                .Append("Confirm your ").Append(app).AppendLine(" account").AppendLine()
                .Append("Hello ").Append(name).AppendLine(",").AppendLine()
                .Append("Somebody used this email address to create a ").Append(app)
                .Append(" account with ").Append(company)
                .AppendLine(". If that was you, confirm the address and choose your password here:").AppendLine()
                .AppendLine(verificationUrl).AppendLine()
                .Append("This link expires in ").Append(hours)
                .AppendLine(" hours and can be used once. If it has expired, request a new one from the sign-in page.")
                .AppendLine()
                .AppendLine("If you did not sign up, no account has been opened in your name and no password exists.")
                .Append("You can safely ignore this email")
                .Append(support == null ? "." : $", or tell us at {support}.")
                .AppendLine().AppendLine()
                .Append(company);

            return new EmailMessage
            {
                To = recipientEmail,
                ToName = recipientName,
                Subject = $"Confirm your {app} account",
                HtmlBody = html.ToString(),
                TextBody = text.ToString()
            };
        }
    }
}
