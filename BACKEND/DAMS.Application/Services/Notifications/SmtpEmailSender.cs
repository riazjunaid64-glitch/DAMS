using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// SMTP transport for transactional mail. Everything it needs comes from the admin
    /// settings store, so swapping provider (or host) is a settings change, and a different
    /// transport altogether is a different <see cref="IEmailSender"/> — no workflow, template
    /// or notification record changes either way.
    /// </summary>
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly NotificationSettingsStore _settings;

        public SmtpEmailSender(NotificationSettingsStore settings)
        {
            _settings = settings;
        }

        public string ProviderName => "smtp";

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var host = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpHost, cancellationToken);
            var senderAddress = await _settings.GetAsync(NotificationSettingKeys.EmailSenderAddress, cancellationToken);

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(senderAddress))
            {
                return new EmailSendResult
                {
                    Success = false,
                    // A missing configuration is not something a retry can fix; it needs an
                    // admin, and it should show up as such in delivery history.
                    IsPermanent = true,
                    Error = "Email is not configured: set the SMTP host and the sender address."
                };
            }

            if (!IsValidAddress(message.To))
                return new EmailSendResult { Success = false, IsPermanent = true, IsHardBounce = true, Error = "The recipient address is not valid." };

            var port = await _settings.GetIntAsync(NotificationSettingKeys.EmailSmtpPort, 587, cancellationToken);
            var useSsl = await _settings.GetBoolAsync(NotificationSettingKeys.EmailSmtpUseSsl, true, cancellationToken);
            var username = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpUsername, cancellationToken);
            var password = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpPassword, cancellationToken);
            var senderName = await _settings.GetOrDefaultAsync(NotificationSettingKeys.EmailSenderName, "DAMS", cancellationToken);
            var replyTo = await _settings.GetAsync(NotificationSettingKeys.EmailReplyTo, cancellationToken);

            using var mail = new MailMessage
            {
                From = new MailAddress(senderAddress, senderName),
                Subject = Sanitize(message.Subject),
                Body = message.TextBody,
                IsBodyHtml = false
            };

            mail.To.Add(new MailAddress(message.To, string.IsNullOrWhiteSpace(message.ToName) ? message.To : message.ToName));

            if (!string.IsNullOrWhiteSpace(replyTo) && IsValidAddress(replyTo))
                mail.ReplyToList.Add(new MailAddress(replyTo));

            // Both bodies are offered; a client that cannot render HTML still gets a readable
            // message rather than markup.
            var htmlView = AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, MediaTypeNames.Text.Html);
            mail.AlternateViews.Add(htmlView);

            var streams = new List<Stream>();
            try
            {
                foreach (var attachment in message.Attachments)
                {
                    var stream = new MemoryStream(attachment.Content);
                    streams.Add(stream);
                    mail.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
                }

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = useSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30_000
                };

                if (!string.IsNullOrWhiteSpace(username))
                {
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(username, password ?? string.Empty);
                }

                await client.SendMailAsync(mail, cancellationToken);

                return new EmailSendResult
                {
                    Success = true,
                    // SMTP acceptance is not proof of delivery, so the caller records this as
                    // "sent", never as "delivered".
                    ProviderReference = mail.Headers["Message-ID"]
                };
            }
            catch (SmtpFailedRecipientException ex)
            {
                var permanent = IsPermanentStatus(ex.StatusCode);
                return new EmailSendResult
                {
                    Success = false,
                    IsPermanent = permanent,
                    IsHardBounce = permanent,
                    Error = $"The address was rejected ({ex.StatusCode}): {ex.Message}"
                };
            }
            catch (SmtpException ex)
            {
                var permanent = IsPermanentStatus(ex.StatusCode);
                return new EmailSendResult
                {
                    Success = false,
                    IsPermanent = permanent,
                    Error = $"SMTP error ({ex.StatusCode}): {ex.Message}"
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Network problems, DNS, TLS: worth another attempt later.
                return new EmailSendResult { Success = false, Error = ex.Message };
            }
            finally
            {
                foreach (var stream in streams)
                    await stream.DisposeAsync();
            }
        }

        /// <summary>5xx-equivalent SMTP conditions: the message will never be accepted as-is.</summary>
        private static bool IsPermanentStatus(SmtpStatusCode code) => code is
            SmtpStatusCode.MailboxNameNotAllowed or
            SmtpStatusCode.MailboxUnavailable or
            SmtpStatusCode.UserNotLocalWillForward or
            SmtpStatusCode.UserNotLocalTryAlternatePath or
            SmtpStatusCode.ExceededStorageAllocation or
            SmtpStatusCode.ClientNotPermitted or
            SmtpStatusCode.MustIssueStartTlsFirst or
            SmtpStatusCode.CommandNotImplemented or
            SmtpStatusCode.CommandParameterNotImplemented or
            SmtpStatusCode.SyntaxError;

        public static bool IsValidAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address) || address.Length > 200)
                return false;

            // A control character in an address is a header-injection attempt, not a typo.
            if (address.Any(char.IsControl) || address.Contains(',') || address.Contains(';'))
                return false;

            try
            {
                var parsed = new MailAddress(address.Trim());
                return parsed.Address.Equals(address.Trim(), StringComparison.OrdinalIgnoreCase)
                       && parsed.Host.Contains('.');
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>A subject line may never contain a line break — that is how headers get forged.</summary>
        private static string Sanitize(string subject) =>
            LeadContactNormalizer.Limit(
                new string(subject.Where(c => !char.IsControl(c)).ToArray()).Trim(), 300);
    }
}
