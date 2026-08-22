using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Issues staff activation invitations. The token is a high-entropy opaque secret that
    /// exists in memory only long enough to build the activation link — the database keeps
    /// nothing but its SHA-256 hash, and this service deliberately has no logger so no code
    /// path can ever write the token or the link to a log.
    ///
    /// Staff activation is a security message, not a customer notification: it goes straight
    /// to <see cref="IEmailSender"/> rather than through recipient resolution, preferences or
    /// the email.enabled toggle, all of which govern optional customer mail.
    ///
    /// It is also the only place a token is spent. Hashing, lookup, expiry, revocation and the
    /// Invited → Active transition all live here, so no caller can assemble its own half of the
    /// check and get it subtly wrong.
    /// </summary>
    public sealed class StaffInvitationService : IStaffInvitationService
    {
        /// <summary>An activation link is a credential, so it expires quickly.</summary>
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);

        /// <summary>The frontend route that trades the token for a chosen password.</summary>
        public const string ActivationPath = "/activate-account";

        /// <summary>
        /// The token travels in the URL fragment, never the query string. A fragment is not part
        /// of the HTTP request: the web server, any reverse proxy and any CDN in front of DAMS
        /// receive only <c>GET /activate-account</c>, so the credential cannot be written to an
        /// access log before a single line of JavaScript has run — which is the whole window the
        /// page's own scrubbing cannot reach. Browsers also strip the fragment from the Referer
        /// of anything the page subsequently loads.
        /// </summary>
        public const string TokenParameter = "token";

        /// <summary>The floor the staff workflow has always had. Wider password policy is an
        /// application-level concern and is deliberately not redefined here.</summary>
        public const int MinPasswordLength = 8;

        /// <summary>
        /// bcrypt hashes only the first 72 bytes of its input. Two longer passwords sharing a
        /// 72-byte prefix would open the same account, so DAMS refuses the password rather than
        /// quietly shortening one the employee believes is longer.
        /// </summary>
        public const int MaxPasswordBytes = 72;

        /// <summary>256 bits of randomness — the token is guessed, never derived from an ID.</summary>
        private const int TokenBytes = 32;

        /// <summary>An issued token is 43 characters. Anything far longer is not a near miss,
        /// so it is dropped before it costs a hash or a query.</summary>
        private const int MaxTokenLength = 200;

        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly IEmailSender _email;
        private readonly TimeProvider _clock;

        public StaffInvitationService(
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

        public Task<StaffInvitationResult> IssueAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken = default) =>
            IssueInternalAsync(userId, invitedByUserId, cancellationToken);

        /// <summary>A resend is an issue: same rules, fresh token, previous one revoked.</summary>
        public Task<StaffInvitationResult> ResendAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken = default) =>
            IssueInternalAsync(userId, invitedByUserId, cancellationToken);

        /// <summary>
        /// Spends a token. See <see cref="IStaffInvitationService.ActivateAsync"/> for the
        /// contract; the shape of the work is: reject cheaply, hash outside the transaction,
        /// then re-check and commit everything as one write.
        /// </summary>
        public async Task<StaffActivationResult> ActivateAsync(
            string rawToken, string chosenPassword, CancellationToken cancellationToken = default)
        {
            // Nothing here touches the database or the CPU-expensive hash, so junk input
            // costs a public endpoint almost nothing.
            if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > MaxTokenLength)
                return StaffActivationResult.InvalidInvitation();

            var passwordProblem = ValidatePassword(chosenPassword);
            if (passwordProblem != null)
                return passwordProblem;

            var tokenHash = HashToken(rawToken);
            var now = _clock.GetUtcNow().UtcDateTime;

            // A first look outside the transaction: a guessed or stale token is turned away by
            // one indexed read, without paying for a bcrypt hash or a serialisable lock. It
            // decides nothing — the check that counts is the one inside the transaction.
            if (await FindUsableAsync(tokenHash, now, cancellationToken) == null)
                return StaffActivationResult.InvalidInvitation();

            // bcrypt is slow on purpose and depends on nothing the transaction reads, so it is
            // done before the transaction opens rather than while holding locks.
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(chosenPassword);

            var result = StaffActivationResult.InvalidInvitation();
            await ExecuteResilientlyAsync(async () =>
            {
                // The strategy can re-run this whole block after a deadlock, so it has to start
                // from what is stored rather than from anything a previous attempt tracked.
                _context.ChangeTracker.Clear();
                await using var transaction = await BeginActivationAsync(cancellationToken);

                // Read the clock again rather than reusing the one from the first look: a retry
                // must not judge expiry against a time that has since passed.
                var attemptedAt = _clock.GetUtcNow().UtcDateTime;

                // Re-read under the transaction. This is where single use is actually enforced:
                // the loser of a race either waits for the winner's commit and then sees an
                // accepted invitation, or is picked as the deadlock victim and retries into
                // exactly the same outcome.
                var invitation = await FindUsableAsync(tokenHash, attemptedAt, cancellationToken);
                if (invitation == null)
                {
                    result = StaffActivationResult.InvalidInvitation();
                    return;
                }

                var user = invitation.User;
                user.Password = passwordHash;
                user.AccountStatus = UserAccountStatus.Active;

                // An Invited login should have no session at all. Clearing anyway means a
                // historic or imported row cannot carry an old session into a fresh account.
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;

                invitation.AcceptedAt = attemptedAt;

                // Any link still outstanding dies with this one, so an older email in the same
                // inbox cannot be replayed later to change the password again.
                var superseded = await _context.StaffInvitations
                    .Where(i => i.UserId == user.UserId && i.Id != invitation.Id
                        && i.AcceptedAt == null && i.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var stale in superseded)
                    stale.RevokedAt = attemptedAt;

                // One SaveChanges for the password, the status and the consumed invitation:
                // there is no instant where the account is usable and the link still is too.
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                result = StaffActivationResult.Success();
            });

            return result;
        }

        /// <summary>
        /// The invitation a token opens, or null for every reason it might not: unknown,
        /// expired, revoked, already spent, a login that has since been activated, disabled or
        /// removed, or an employee who no longer works here. One return value for all of them,
        /// because the caller must not be able to tell them apart.
        /// </summary>
        private async Task<StaffInvitation?> FindUsableAsync(
            string tokenHash, DateTime now, CancellationToken cancellationToken)
        {
            var invitation = await _context.StaffInvitations
                .Include(i => i.User)
                .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

            if (invitation == null
                || invitation.AcceptedAt != null
                || invitation.RevokedAt != null
                || invitation.ExpiresAt <= now)
                return null;

            // Only a login still waiting for its first password can be activated. An Active or
            // Disabled account is never reached through a link.
            if (invitation.User == null || invitation.User.AccountStatus != UserAccountStatus.Invited)
                return null;

            // An invitation is not a standing offer. Somebody whose employment ended after the
            // link was sent must not be able to open a DAMS login with yesterday's email.
            var stillEmployed = await _context.Employees.AnyAsync(
                e => e.UserId == invitation.UserId && e.Status == EmployeeStatus.Active,
                cancellationToken);

            return stillEmployed ? invitation : null;
        }

        /// <summary>
        /// Serialisable for issuance: "read the user as Invited, read invitations as outstanding,
        /// then revoke and insert new" is only single-use if nothing can slip between the three
        /// steps. Skipped on a non-relational provider, which has no concurrency to protect
        /// against, and skipped inside a caller's transaction rather than nesting one.
        /// </summary>
        private async Task<IDbContextTransaction?> BeginIssuanceAsync(CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
                return null;

            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        /// <summary>
        /// Serialisable, because "read it as unused, then write it as used" is only single-use
        /// if nothing can slip between the two. Skipped on a non-relational provider, which has
        /// no concurrency to protect against, and skipped inside a caller's transaction rather
        /// than nesting one.
        /// </summary>
        private async Task<IDbContextTransaction?> BeginActivationAsync(CancellationToken cancellationToken)
        {
            if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
                return null;

            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        /// <summary>
        /// Runs the activation as one retriable unit. Not optional: the API configures SQL
        /// Server with EnableRetryOnFailure, and EF refuses to run anything inside a
        /// caller-opened transaction unless it goes through the execution strategy.
        /// </summary>
        private Task ExecuteResilientlyAsync(Func<Task> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

        /// <summary>The password rules, as the two things the employee can actually fix.</summary>
        private static StaffActivationResult? ValidatePassword(string? chosenPassword)
        {
            if (string.IsNullOrEmpty(chosenPassword) || chosenPassword.Length < MinPasswordLength)
                return StaffActivationResult.Rejected(
                    StaffActivationFailure.PasswordTooShort,
                    $"Choose a password of at least {MinPasswordLength} characters.");

            // Deliberately no number in this message. The limit is 72 *bytes*, and "72
            // characters" would be a lie to anybody typing accented letters or emoji — a
            // 40-character password can be well over the boundary.
            if (Encoding.UTF8.GetByteCount(chosenPassword) > MaxPasswordBytes)
                return StaffActivationResult.Rejected(
                    StaffActivationFailure.PasswordTooLong,
                    "That password is too long. Shorten it and try again.");

            return null;
        }

        /// <summary>
        /// The lookup form of a token: the raw value never has to be stored to be verified.
        /// Private on purpose — a second implementation of "is this token good?" somewhere
        /// else in DAMS is exactly the bug this service exists to prevent.
        /// </summary>
        private static string HashToken(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        /// <summary>
        /// What the transactional half of issuance produced, so the mailing half can run with no
        /// database work left to do. The raw token lives here and nowhere else.
        /// </summary>
        private sealed record MintedInvitation(
            int InvitationId, DateTime ExpiresAt, string RawToken, string Email, string FullName);

        private async Task<StaffInvitationResult> IssueInternalAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken)
        {
            // Split deliberately. Everything that decides and writes happens in the retriable
            // serialisable block below; the mail is sent afterwards, outside both the transaction
            // and the retry, so a deadlock retry can never send a second copy of a link.
            StaffInvitationResult? rejected = null;
            MintedInvitation? minted = null;

            await ExecuteResilientlyAsync(async () =>
            {
                // A retry re-runs this whole block, so it has to start from what is stored.
                _context.ChangeTracker.Clear();
                rejected = null;
                minted = null;

                await using var transaction = await BeginIssuanceAsync(cancellationToken);

                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
                if (user == null)
                {
                    rejected = StaffInvitationResult.Rejected(
                        StaffInvitationFailure.UserNotFound, "That login no longer exists.");
                    return;
                }

                // Granting, disabling and activating an account are transitions owned by their own
                // workflows. An invitation may only be minted for a login that is already waiting
                // for one, so this service can never hand an activation link to a working account.
                // Read inside the transaction: a concurrent activation that commits first must be
                // seen here, or this would mint a link for an account that is already Active.
                if (user.AccountStatus != UserAccountStatus.Invited)
                {
                    rejected = StaffInvitationResult.Rejected(
                        StaffInvitationFailure.AccountNotInvitable,
                        user.AccountStatus == UserAccountStatus.Active
                            ? "That account is already active and does not need an activation link."
                            : "That account is disabled. Re-enable it before sending an activation link.");
                    return;
                }

                // The same employment gate activation applies. Without it DAMS emails a credential
                // that FindUsableAsync is guaranteed to refuse — a link that could never work.
                var stillEmployed = await _context.Employees.AnyAsync(
                    e => e.UserId == userId && e.Status == EmployeeStatus.Active,
                    cancellationToken);

                if (!stillEmployed)
                {
                    rejected = StaffInvitationResult.Rejected(
                        StaffInvitationFailure.EmployeeNotActive,
                        "That employee is not currently active, so an activation link would not work. "
                        + "Set their employment back to Active first, then send the invitation.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    rejected = StaffInvitationResult.Rejected(
                        StaffInvitationFailure.MissingEmail, "That login has no email address to send an invitation to.");
                    return;
                }

                var now = _clock.GetUtcNow().UtcDateTime;
                var rawToken = GenerateToken();

                // Read-then-write over the outstanding set, which is only "at most one usable
                // invitation" if nothing can insert between the read and the write — hence the
                // serialisable transaction rather than SaveChanges' own implicit one.
                var outstanding = await _context.StaffInvitations
                    .Where(i => i.UserId == userId && i.AcceptedAt == null && i.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var superseded in outstanding)
                    superseded.RevokedAt = now;

                var invitation = new StaffInvitation
                {
                    UserId = user.UserId,
                    InvitedByUserId = invitedByUserId,
                    TokenHash = HashToken(rawToken),
                    CreatedAt = now,
                    ExpiresAt = now.Add(InvitationLifetime)
                };

                _context.StaffInvitations.Add(invitation);
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                minted = new MintedInvitation(
                    invitation.Id, invitation.ExpiresAt, rawToken, user.Email, user.FullName);
            });

            if (rejected != null)
                return rejected;

            // Committed. Everything below is best-effort delivery: the invitation stays stored
            // either way, the account is not activated, no password exists, and an Admin can resend.
            var issued = minted!;
            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var activationUrl = BuildActivationUrl(branding.PublicBaseUrl, issued.RawToken);
            if (activationUrl == null)
                return StaffInvitationResult.Undelivered(
                    issued.InvitationId, issued.ExpiresAt, StaffInvitationFailure.PublicBaseUrlNotConfigured,
                    "The public site address is not configured, so no activation link could be sent. "
                    + "Set it in notification settings, then resend the invitation.");

            var send = await _email.SendAsync(
                BuildEmail(issued.Email, issued.FullName, branding, activationUrl), cancellationToken);

            return send.Success
                ? StaffInvitationResult.Delivered(issued.InvitationId, issued.ExpiresAt)
                : StaffInvitationResult.Undelivered(
                    issued.InvitationId, issued.ExpiresAt, StaffInvitationFailure.EmailDeliveryFailed,
                    send.Error ?? "The activation email could not be delivered.");
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
        /// The link origin comes only from the Admin-configured public base URL — never from
        /// a request Host, Origin or Referer header, which the browser controls. Returns null
        /// when nothing trustworthy is configured, so a broken link is never emailed.
        /// </summary>
        private static string? BuildActivationUrl(string? baseUrl, string rawToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var origin)
                || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
                return null;

            // Authority never carries a trailing slash, so a configured "https://host/" and
            // "https://host" produce the same single-slash link. The token goes after the '#'
            // for the reason given on TokenParameter: nothing before the fragment is secret,
            // and nothing after it is ever sent to a server.
            return $"{origin.GetLeftPart(UriPartial.Authority)}{ActivationPath}"
                + $"#{TokenParameter}={Uri.EscapeDataString(rawToken)}";
        }

        /// <summary>
        /// A deliberately plain security email: an identity, a reason, one link, an expiry
        /// and a way to raise the alarm. No password, no credential and no internal ID.
        /// </summary>
        private static EmailMessage BuildEmail(
            string recipientEmail, string recipientName, NotificationBranding branding, string activationUrl)
        {
            var company = branding.CompanyName;
            var app = branding.AppName;
            var name = string.IsNullOrWhiteSpace(recipientName) ? "there" : recipientName;
            var hours = (int)InvitationLifetime.TotalHours;
            var support = branding.SupportEmail;

            var safeName = WebUtility.HtmlEncode(name);
            var safeCompany = WebUtility.HtmlEncode(company);
            var safeApp = WebUtility.HtmlEncode(app);
            var safeUrl = WebUtility.HtmlEncode(activationUrl);
            var safeSupport = support == null ? null : WebUtility.HtmlEncode(support);

            var html = new StringBuilder()
                .Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>Activate your ").Append(safeApp).Append(" account</title></head>")
                .Append("<body style=\"margin:0;padding:0;background:#f4f5f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2933;\">")
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f4f5f7;padding:24px 12px;\"><tr><td align=\"center\">")
                .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:12px;overflow:hidden;\">")
                .Append("<tr><td style=\"background:").Append(branding.PrimaryColor).Append(";padding:20px 24px;\">")
                .Append("<span style=\"color:#ffffff;font-size:18px;font-weight:600;\">").Append(safeCompany).Append("</span>")
                .Append("</td></tr>")
                .Append("<tr><td style=\"padding:24px;font-size:15px;line-height:1.6;color:#334e68;\">")
                .Append("<h1 style=\"margin:0 0 12px;font-size:20px;line-height:1.3;color:#102a43;\">Activate your ")
                .Append(safeApp).Append(" account</h1>")
                .Append("<p style=\"margin:0 0 12px;\">Hello ").Append(safeName).Append(",</p>")
                .Append("<p style=\"margin:0 0 12px;\">").Append(safeCompany)
                .Append(" has created a ").Append(safeApp)
                .Append(" account for you. To finish setting it up, choose your own password using the link below. ")
                .Append("Nobody at ").Append(safeCompany).Append(" knows or can see your password.</p>")
                .Append("<div style=\"margin:24px 0 4px;\"><a href=\"").Append(safeUrl)
                .Append("\" style=\"display:inline-block;background:").Append(branding.AccentColor)
                .Append(";color:#1a1a1a;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:8px;font-size:15px;\">")
                .Append("Activate my account</a></div>")
                .Append("<p style=\"font-size:12px;color:#829ab1;margin:12px 0 0;word-break:break-all;\">")
                .Append("If the button does not work, open: ").Append(safeUrl).Append("</p>")
                .Append("<p style=\"margin:20px 0 0;\">This link expires in ").Append(hours)
                .Append(" hours and can be used once. If it has expired, ask your administrator to send a new one.</p>")
                .Append("<p style=\"margin:12px 0 0;color:#52606d;\">If you were not expecting this invitation, do not use the link");
            if (safeSupport != null)
                html.Append(" and contact us at <a href=\"mailto:").Append(safeSupport)
                    .Append("\" style=\"color:#334e68;\">").Append(safeSupport).Append("</a>");
            html.Append(".</p>")
                .Append("</td></tr>")
                .Append("<tr><td style=\"background:#f8f9fb;padding:18px 24px;font-size:12px;color:#627d98;border-top:1px solid #e4e7eb;\">")
                .Append(safeCompany)
                .Append("</td></tr></table></td></tr></table></body></html>");

            var text = new StringBuilder()
                .Append("Activate your ").Append(app).AppendLine(" account").AppendLine()
                .Append("Hello ").Append(name).AppendLine(",").AppendLine()
                .Append(company).Append(" has created a ").Append(app)
                .AppendLine(" account for you. To finish setting it up, choose your own password here:").AppendLine()
                .AppendLine(activationUrl).AppendLine()
                .Append("This link expires in ").Append(hours)
                .AppendLine(" hours and can be used once. If it has expired, ask your administrator to send a new one.")
                .AppendLine()
                .Append("If you were not expecting this invitation, do not use the link")
                .Append(support == null ? "." : $" and contact us at {support}.")
                .AppendLine().AppendLine()
                .Append(company);

            return new EmailMessage
            {
                To = recipientEmail,
                ToName = recipientName,
                Subject = $"Activate your {app} account",
                HtmlBody = html.ToString(),
                TextBody = text.ToString()
            };
        }
    }
}
