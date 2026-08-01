using System.Net.Sockets;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Provider-neutral SMTP transport. Workflows depend only on <see cref="IEmailSender"/>;
    /// provider host, port, TLS and credentials remain runtime settings.
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
            var provider = await _settings.GetOrDefaultAsync(NotificationSettingKeys.EmailProvider, "smtp", cancellationToken);
            var host = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpHost, cancellationToken);
            var senderAddress = await _settings.GetAsync(NotificationSettingKeys.EmailSenderAddress, cancellationToken);
            var username = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpUsername, cancellationToken);
            var password = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpPassword, cancellationToken);

            var configurationError = ValidateConfiguration(provider, host, senderAddress, username, password);
            if (configurationError != null)
                return Permanent(configurationError);

            if (!IsValidAddress(message.To))
                return Permanent("The recipient address is not valid.", hardBounce: true);

            var port = await _settings.GetIntAsync(NotificationSettingKeys.EmailSmtpPort, 587, cancellationToken);
            if (port is < 1 or > 65535)
                return Permanent("The SMTP port must be between 1 and 65535.");

            var useTls = await _settings.GetBoolAsync(NotificationSettingKeys.EmailSmtpUseSsl, true, cancellationToken);
            var senderName = await _settings.GetOrDefaultAsync(NotificationSettingKeys.EmailSenderName, "DAMS", cancellationToken);
            var replyTo = await _settings.GetAsync(NotificationSettingKeys.EmailReplyTo, cancellationToken);
            if (replyTo != null && !IsValidAddress(replyTo))
                return Permanent("The reply-to address is not valid.");

            try
            {
                var mail = BuildMessage(message, senderAddress!, senderName, replyTo);
                using var client = new SmtpClient { Timeout = 30_000 };

                // Port 465 is implicit TLS. Other TLS ports use mandatory STARTTLS rather
                // than opportunistic encryption, so a downgrade cannot silently send plain.
                var socketOptions = !useTls
                    ? SecureSocketOptions.None
                    : port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

                await client.ConnectAsync(host!, port, socketOptions, cancellationToken);
                if (username != null)
                    await client.AuthenticateAsync(username, password!, cancellationToken);

                await client.SendAsync(mail, cancellationToken);

                // The provider has accepted the message. A broken QUIT/disconnect handshake
                // must not turn that success into a retry and send the same email twice.
                try
                {
                    await client.DisconnectAsync(true, CancellationToken.None);
                }
                catch
                {
                    // Disposal closes the socket; acceptance remains the authoritative result.
                }

                return new EmailSendResult
                {
                    Success = true,
                    // SMTP acceptance is not proof of final delivery.
                    ProviderReference = mail.MessageId
                };
            }
            catch (SmtpCommandException ex)
            {
                var permanent = (int)ex.StatusCode >= 500;
                var recipientRejected = ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted;
                return new EmailSendResult
                {
                    Success = false,
                    IsPermanent = permanent,
                    IsHardBounce = permanent && recipientRejected,
                    Error = $"The SMTP server rejected the message ({(int)ex.StatusCode}, {ex.ErrorCode})."
                };
            }
            catch (MailKit.Security.AuthenticationException)
            {
                return Permanent("SMTP authentication failed. Check the dedicated SMTP username and password.");
            }
            catch (SslHandshakeException)
            {
                return Permanent("The SMTP TLS handshake failed. Check the host, port, TLS mode and server certificate.");
            }
            catch (SmtpProtocolException)
            {
                return Transient("The SMTP server returned an invalid or incomplete response.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Transient("The SMTP connection timed out.");
            }
            catch (SocketException)
            {
                return Transient("The SMTP server could not be reached. Check DNS, firewall and provider availability.");
            }
            catch (IOException)
            {
                return Transient("The SMTP connection ended before the message was accepted.");
            }
            catch (FormatException)
            {
                return Permanent("An email header or attachment content type is not valid.");
            }
            catch (ArgumentException)
            {
                return Permanent("An email header, SMTP setting or attachment is not valid.");
            }
        }

        private static MimeMessage BuildMessage(
            EmailMessage message, string senderAddress, string senderName, string? replyTo)
        {
            var mail = new MimeMessage
            {
                Subject = Sanitize(message.Subject)
            };
            mail.From.Add(new MailboxAddress(Sanitize(senderName), senderAddress));
            mail.To.Add(new MailboxAddress(Sanitize(message.ToName ?? string.Empty), message.To));
            if (replyTo != null)
                mail.ReplyTo.Add(MailboxAddress.Parse(replyTo));

            var body = new BodyBuilder
            {
                TextBody = message.TextBody,
                HtmlBody = message.HtmlBody
            };
            foreach (var attachment in message.Attachments)
            {
                var fileName = Sanitize(Path.GetFileName(attachment.FileName));
                body.Attachments.Add(fileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            }

            mail.Body = body.ToMessageBody();
            return mail;
        }

        private static string? ValidateConfiguration(
            string provider, string? host, string? senderAddress, string? username, string? password)
        {
            if (!provider.Equals("smtp", StringComparison.OrdinalIgnoreCase))
                return "The configured email provider is not supported.";
            if (!IsValidHost(host))
                return "Email is not configured: set a valid SMTP host without a URL scheme or path.";
            if (!IsValidAddress(senderAddress))
                return "Email is not configured: set a valid sender address.";
            if ((username == null) != (password == null))
                return "SMTP username and password must either both be set or both be empty.";
            return null;
        }

        public static bool IsValidHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsControl))
                return false;

            var trimmed = host.Trim();
            return trimmed == host
                   && !trimmed.Contains("://", StringComparison.Ordinal)
                   && !trimmed.Contains('/')
                   && Uri.CheckHostName(trimmed) != UriHostNameType.Unknown;
        }

        public static bool IsValidAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address) || address.Length > 200)
                return false;
            if (address.Any(char.IsControl) || address.Contains(',') || address.Contains(';'))
                return false;

            try
            {
                var parsed = new System.Net.Mail.MailAddress(address.Trim());
                return parsed.Address.Equals(address.Trim(), StringComparison.OrdinalIgnoreCase)
                       && parsed.Host.Contains('.');
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static EmailSendResult Permanent(string error, bool hardBounce = false) => new()
        {
            Success = false,
            IsPermanent = true,
            IsHardBounce = hardBounce,
            Error = error
        };

        private static EmailSendResult Transient(string error) => new()
        {
            Success = false,
            IsPermanent = false,
            Error = error
        };

        private static string Sanitize(string value) =>
            LeadContactNormalizer.Limit(
                new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim(), 300);
    }
}
