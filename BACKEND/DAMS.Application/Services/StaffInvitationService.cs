using System.Net;
using System.Security.Cryptography;
using System.Text;
using DAMS.Application.Interfaces;
using DAMS.Application.Services.Notifications;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
    /// </summary>
    public sealed class StaffInvitationService : IStaffInvitationService
    {
        /// <summary>An activation link is a credential, so it expires quickly.</summary>
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);

        /// <summary>The frontend route that trades the token for a chosen password.</summary>
        public const string ActivationPath = "/activate-account";

        /// <summary>256 bits of randomness — the token is guessed, never derived from an ID.</summary>
        private const int TokenBytes = 32;

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
        /// The lookup form of a token. The activation phase hashes what it was given and
        /// matches on that, so the raw value never has to be stored to be verified.
        /// </summary>
        public static string HashToken(string rawToken) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        private async Task<StaffInvitationResult> IssueInternalAsync(
            int userId, int invitedByUserId, CancellationToken cancellationToken)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null)
                return StaffInvitationResult.Rejected(
                    StaffInvitationFailure.UserNotFound, "That login no longer exists.");

            // Granting, disabling and activating an account are transitions owned by their own
            // workflows. An invitation may only be minted for a login that is already waiting
            // for one, so this service can never hand an activation link to a working account.
            if (user.AccountStatus != UserAccountStatus.Invited)
                return StaffInvitationResult.Rejected(
                    StaffInvitationFailure.AccountNotInvitable,
                    user.AccountStatus == UserAccountStatus.Active
                        ? "That account is already active and does not need an activation link."
                        : "That account is disabled. Re-enable it before sending an activation link.");

            if (string.IsNullOrWhiteSpace(user.Email))
                return StaffInvitationResult.Rejected(
                    StaffInvitationFailure.MissingEmail, "That login has no email address to send an invitation to.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var rawToken = GenerateToken();

            // Revoking the old invitations and creating the replacement go out in one
            // SaveChanges, which EF wraps in its own transaction — there is no instant at
            // which two tokens are outstanding, and no transaction is held open across SMTP.
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

            // Everything below is best-effort delivery. The invitation stays committed either
            // way: the account is not activated, no password exists, and an Admin can resend.
            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var activationUrl = BuildActivationUrl(branding.PublicBaseUrl, rawToken);
            if (activationUrl == null)
                return StaffInvitationResult.Undelivered(
                    invitation.Id, invitation.ExpiresAt, StaffInvitationFailure.PublicBaseUrlNotConfigured,
                    "The public site address is not configured, so no activation link could be sent. "
                    + "Set it in notification settings, then resend the invitation.");

            var send = await _email.SendAsync(
                BuildEmail(user, branding, activationUrl), cancellationToken);

            return send.Success
                ? StaffInvitationResult.Delivered(invitation.Id, invitation.ExpiresAt)
                : StaffInvitationResult.Undelivered(
                    invitation.Id, invitation.ExpiresAt, StaffInvitationFailure.EmailDeliveryFailed,
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
            // "https://host" produce the same single-slash link.
            return $"{origin.GetLeftPart(UriPartial.Authority)}{ActivationPath}?token={Uri.EscapeDataString(rawToken)}";
        }

        /// <summary>
        /// A deliberately plain security email: an identity, a reason, one link, an expiry
        /// and a way to raise the alarm. No password, no credential and no internal ID.
        /// </summary>
        private static EmailMessage BuildEmail(User user, NotificationBranding branding, string activationUrl)
        {
            var company = branding.CompanyName;
            var app = branding.AppName;
            var name = string.IsNullOrWhiteSpace(user.FullName) ? "there" : user.FullName;
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
                To = user.Email,
                ToName = user.FullName,
                Subject = $"Activate your {app} account",
                HtmlBody = html.ToString(),
                TextBody = text.ToString()
            };
        }
    }
}
