using DAMS.Application.Common;
using DAMS.Application.DTOs.NotificationDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Admin configuration: branding, provider settings, templates and rules.
    ///
    /// Secrets go in but never come out: a stored password or private key is only ever
    /// returned as a masked hint, and submitting the mask back leaves the stored value
    /// untouched, so an admin can save the form without having to retype every secret.
    /// </summary>
    public sealed class NotificationConfigurationService : INotificationConfigurationService
    {
        /// <summary>Sent instead of a secret, and recognised on the way back in.</summary>
        public const string SecretPlaceholder = "********";

        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;
        private readonly NotificationRenderer _renderer;
        private readonly IEmailSender _email;

        public NotificationConfigurationService(
            AppDbContext context,
            NotificationSettingsStore settings,
            NotificationRenderer renderer,
            IEmailSender email)
        {
            _context = context;
            _settings = settings;
            _renderer = renderer;
            _email = email;
        }

        // ── Settings ────────────────────────────────────────────────────────────────

        public async Task<NotificationSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _context.NotificationSettings.AsNoTracking().ToListAsync(cancellationToken);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (NotificationSettingKeys.IsSecret(row.Key))
                {
                    if (!string.IsNullOrWhiteSpace(row.Value))
                        secrets[row.Key] = Mask(row.Value!);
                    continue;
                }

                values[row.Key] = row.Value;
            }

            // The public half of the VAPID pair is meant to be public — the browser needs it
            // to subscribe — but it is only exposed here, never alongside its private half.
            return new NotificationSettingsDto
            {
                Values = values,
                Secrets = secrets,
                Status = await BuildStatusAsync(cancellationToken)
            };
        }

        public async Task<NotificationSettingsDto> UpdateSettingsAsync(
            UpdateNotificationSettingsDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var effective = (await _settings.AllAsync(cancellationToken))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in dto.Values)
            {
                if (!NotificationSettingKeys.Writable.Contains(pair.Key))
                    throw new InvalidOperationException($"'{pair.Key}' is not a notification setting that can be changed.");
                if (NotificationSettingKeys.IsSecret(pair.Key) && pair.Value == SecretPlaceholder)
                    continue;

                Validate(pair.Key, pair.Value);
                effective[pair.Key] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
            }
            ValidateEmailConfiguration(effective);

            var changed = new List<string>();

            foreach (var pair in dto.Values)
            {
                // An allow-list, not a deny-list: an unknown key is refused outright so a
                // crafted request cannot write arbitrary configuration.
                if (!NotificationSettingKeys.Writable.Contains(pair.Key))
                    throw new InvalidOperationException($"'{pair.Key}' is not a notification setting that can be changed.");

                var value = pair.Value;

                if (NotificationSettingKeys.IsSecret(pair.Key))
                {
                    // The form round-trips the mask; that means "leave it alone".
                    if (value == SecretPlaceholder)
                        continue;

                    changed.Add($"{pair.Key} (secret replaced)");
                    await _settings.SetAsync(pair.Key, value, ctx.UserId, cancellationToken);
                    continue;
                }

                Validate(pair.Key, value);
                changed.Add(pair.Key);
                await _settings.SetAsync(pair.Key, value, ctx.UserId, cancellationToken);
            }

            if (changed.Count > 0)
                Audit(ctx, "settings", "update", string.Join(", ", changed));

            await _context.SaveChangesAsync(cancellationToken);
            return await GetSettingsAsync(cancellationToken);
        }

        /// <summary>Creates a VAPID key pair and stores both halves. The private half never
        /// leaves the server; the caller receives only the public key.</summary>
        public async Task<string> GeneratePushKeysAsync(NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var (publicKey, privateKey) = WebPushClient.GenerateVapidKeys();

            await _settings.SetAsync(NotificationSettingKeys.PushVapidPublicKey, publicKey, ctx.UserId, cancellationToken);
            await _settings.SetAsync(NotificationSettingKeys.PushVapidPrivateKey, privateKey, ctx.UserId, cancellationToken);

            // Existing subscriptions were negotiated against the old key and can no longer be
            // signed for; deactivating them makes every browser re-subscribe cleanly instead
            // of failing silently for ever.
            var existing = await _context.PushSubscriptions.Where(s => s.IsActive).ToListAsync(cancellationToken);
            foreach (var subscription in existing)
            {
                subscription.IsActive = false;
                subscription.DeactivatedAt = DateTime.UtcNow;
                subscription.DeactivationReason = "The application push keys were regenerated.";
            }

            Audit(ctx, "settings", "generate-push-keys",
                $"New VAPID key pair generated; {existing.Count} existing subscription(s) retired.");

            await _context.SaveChangesAsync(cancellationToken);
            return publicKey;
        }

        public async Task<string> SendTestEmailAsync(
            SendTestEmailDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var recipient = LeadContactNormalizer.Clean(dto.Recipient)
                            ?? await _settings.GetAsync(NotificationSettingKeys.EmailTestRecipient, cancellationToken)
                            ?? ctx.Email;

            if (!SmtpEmailSender.IsValidAddress(recipient))
                throw new InvalidOperationException("Enter a valid email address to send the test to.");

            var branding = await _settings.GetBrandingAsync(cancellationToken);
            var sample = new Notification
            {
                Type = NotificationType.AccountSecurity,
                Category = NotificationCategory.AccountAndSecurity,
                Title = $"{branding.AppName} test email",
                Message = "This is a test message from the DAMS notification settings. If you received it, transactional email is working.",
                DeepLink = "/notifications",
                RecipientUserId = ctx.UserId
            };

            var rendered = await _renderer.RenderEmailAsync(sample, ctx.DisplayName ?? "there", cancellationToken);

            var result = await _email.SendAsync(new EmailMessage
            {
                To = recipient!,
                ToName = ctx.DisplayName,
                Subject = rendered.Subject,
                HtmlBody = rendered.Html,
                TextBody = rendered.Text
            }, cancellationToken);

            await _settings.SetAsync(NotificationSettingKeys.EmailTestRecipient, recipient, ctx.UserId, cancellationToken);

            if (result.Success)
            {
                await _settings.SetAsync(NotificationSettingKeys.EmailLastTestAt, DateTime.UtcNow.ToString("O"), ctx.UserId, cancellationToken);
                await _settings.SetAsync(NotificationSettingKeys.EmailLastFailure, null, ctx.UserId, cancellationToken);
                Audit(ctx, "settings", "test-email", $"Test email sent to {EmailChannelSender.Mask(recipient!)}.");
                await _context.SaveChangesAsync(cancellationToken);
                return $"Test email sent to {recipient}.";
            }

            await _settings.SetAsync(NotificationSettingKeys.EmailLastFailure,
                LeadContactNormalizer.LimitOrNull(result.Error, 2000), ctx.UserId, cancellationToken);
            Audit(ctx, "settings", "test-email-failed", LeadContactNormalizer.LimitOrNull(result.Error, 500));
            await _context.SaveChangesAsync(cancellationToken);

            throw new InvalidOperationException(result.Error ?? "The test email could not be sent.");
        }

        private async Task<NotificationStatusDto> BuildStatusAsync(CancellationToken cancellationToken)
        {
            var emailEnabled = await _settings.GetBoolAsync(NotificationSettingKeys.EmailEnabled, false, cancellationToken);
            var provider = await _settings.GetOrDefaultAsync(NotificationSettingKeys.EmailProvider, "smtp", cancellationToken);
            var host = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpHost, cancellationToken);
            var senderAddress = await _settings.GetAsync(NotificationSettingKeys.EmailSenderAddress, cancellationToken);
            var replyTo = await _settings.GetAsync(NotificationSettingKeys.EmailReplyTo, cancellationToken);
            var username = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpUsername, cancellationToken);
            var password = await _settings.GetAsync(NotificationSettingKeys.EmailSmtpPassword, cancellationToken);
            var port = await _settings.GetIntAsync(NotificationSettingKeys.EmailSmtpPort, 587, cancellationToken);

            string? emailIssue = null;
            if (!provider.Equals("smtp", StringComparison.OrdinalIgnoreCase))
                emailIssue = "The selected email provider is not supported.";
            else if (!SmtpEmailSender.IsValidHost(host))
                emailIssue = "The SMTP host is missing or not valid.";
            else if (port is < 1 or > 65535)
                emailIssue = "The SMTP port is not valid.";
            else if (!SmtpEmailSender.IsValidAddress(senderAddress))
                emailIssue = "The sender address is missing or not valid.";
            else if (replyTo != null && !SmtpEmailSender.IsValidAddress(replyTo))
                emailIssue = "The reply-to address is not valid.";
            else if ((username == null) != (password == null))
                emailIssue = "SMTP username and password must both be set, or both be empty.";

            var pushEnabled = await _settings.GetBoolAsync(NotificationSettingKeys.PushEnabled, false, cancellationToken);
            var publicKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPublicKey, cancellationToken);
            var privateKey = await _settings.GetAsync(NotificationSettingKeys.PushVapidPrivateKey, cancellationToken);

            string? pushIssue = null;
            if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
                pushIssue = "No push keys have been generated yet.";
            else if (!PushSubscriptionService.IsValidVapidSubject(
                         await _settings.GetAsync(NotificationSettingKeys.PushVapidSubject, cancellationToken)))
                pushIssue = "Set a valid contact address (mailto: or https:) for the push subject.";

            return new NotificationStatusDto
            {
                EmailEnabled = emailEnabled,
                EmailConfigured = emailIssue == null,
                EmailConfigurationIssue = emailIssue,
                EmailLastTestAt = ParseDate(await _settings.GetAsync(NotificationSettingKeys.EmailLastTestAt, cancellationToken)),
                EmailLastFailure = await _settings.GetAsync(NotificationSettingKeys.EmailLastFailure, cancellationToken),
                PushEnabled = pushEnabled,
                PushConfigured = pushIssue == null,
                PushConfigurationIssue = pushIssue,
                PushLastTestAt = ParseDate(await _settings.GetAsync(NotificationSettingKeys.PushLastTestAt, cancellationToken)),
                PushLastFailure = await _settings.GetAsync(NotificationSettingKeys.PushLastFailure, cancellationToken),
                ActivePushSubscriptions = await _context.PushSubscriptions.CountAsync(s => s.IsActive, cancellationToken),
                PendingDeliveries = await _context.NotificationDeliveries.CountAsync(
                    d => d.Status == NotificationDeliveryStatus.Pending
                         || d.Status == NotificationDeliveryStatus.Retrying
                         || d.Status == NotificationDeliveryStatus.Processing, cancellationToken),
                FailedDeliveries = await _context.NotificationDeliveries.CountAsync(
                    d => d.Status == NotificationDeliveryStatus.Failed || d.Status == NotificationDeliveryStatus.Bounced,
                    cancellationToken)
            };
        }

        // ── Templates ───────────────────────────────────────────────────────────────

        public async Task<List<NotificationTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        {
            var stored = await _context.NotificationTemplates.AsNoTracking().ToListAsync(cancellationToken);
            var editors = await ResolveNamesAsync(stored.Select(t => t.UpdatedByUserId), cancellationToken);

            var result = new List<NotificationTemplateDto>();

            // The catalog, not the table, decides which templates exist — a fresh database
            // still shows every editable template with its built-in wording.
            foreach (var definition in NotificationCatalog.All.OrderBy(d => d.Category).ThenBy(d => d.Name))
            {
                foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.WebPush })
                {
                    if (!definition.DefaultChannels.HasFlag(channel))
                        continue;

                    var template = stored.FirstOrDefault(t => t.Type == definition.Type && t.Channel == channel);
                    result.Add(Map(definition, channel, template, editors));
                }
            }

            return result;
        }

        public async Task<NotificationTemplateDto> GetTemplateAsync(
            NotificationType type, NotificationChannel channel, CancellationToken cancellationToken = default)
        {
            var definition = NotificationCatalog.Get(type);
            var template = await _context.NotificationTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Type == type && t.Channel == channel, cancellationToken);

            var editors = await ResolveNamesAsync(new[] { template?.UpdatedByUserId }, cancellationToken);
            return Map(definition, channel, template, editors);
        }

        public async Task<NotificationTemplateDto> SaveTemplateAsync(
            NotificationType type, NotificationChannel channel, SaveNotificationTemplateDto dto,
            NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            if (channel is not (NotificationChannel.Email or NotificationChannel.WebPush))
                throw new InvalidOperationException("Only email and browser push templates can be edited.");

            var definition = NotificationCatalog.Get(type);
            var variables = channel == NotificationChannel.WebPush
                ? NotificationCatalog.PushVariablesFor(type)
                : NotificationCatalog.VariablesFor(type);

            if (string.IsNullOrWhiteSpace(dto.Subject))
                throw new InvalidOperationException(channel == NotificationChannel.Email
                    ? "An email subject is required." : "A push title is required.");

            if (string.IsNullOrWhiteSpace(dto.Body))
                throw new InvalidOperationException("A message body is required.");

            NotificationTemplateRenderer.Validate(dto.Subject, variables, channel == NotificationChannel.Email ? "Subject" : "Title");
            NotificationTemplateRenderer.Validate(dto.Heading, variables, "Heading");
            NotificationTemplateRenderer.Validate(dto.Body, variables, "Body");
            NotificationTemplateRenderer.Validate(dto.ActionText, variables, "Action button text");
            NotificationTemplateRenderer.Validate(dto.Footer, variables, "Footer");

            var actionUrl = LeadContactNormalizer.Clean(dto.ActionUrl);
            if (actionUrl != null && NotificationLink.Sanitize(actionUrl) == null)
                throw new InvalidOperationException(
                    "The action destination must be a path inside DAMS, for example /notifications.");

            if (!dto.IsEnabled && (definition.IsMandatory || NotificationCatalog.IsMandatoryCategory(definition.Category)))
                throw new InvalidOperationException(
                    $"'{definition.Name}' is an essential notification and its template cannot be disabled.");

            var template = await _context.NotificationTemplates
                .FirstOrDefaultAsync(t => t.Type == type && t.Channel == channel, cancellationToken);

            var isNew = template == null;
            template ??= new NotificationTemplate
            {
                Type = type,
                Channel = channel,
                Category = definition.Category,
                CreatedAt = DateTime.UtcNow,
                Version = 0
            };

            template.Name = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.Name), 150) ?? definition.Name;
            template.Category = definition.Category;
            template.Subject = LeadContactNormalizer.Limit(dto.Subject.Trim(), 300);
            template.Heading = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.Heading), 300);
            template.Body = LeadContactNormalizer.Limit(dto.Body.Trim(), 8000);
            template.ActionText = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.ActionText), 80);
            template.ActionUrl = actionUrl;
            template.Footer = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(dto.Footer), 2000);
            template.IconUrl = LeadContactNormalizer.LimitOrNull(SafeAssetUrl(dto.IconUrl), 500);
            template.BadgeUrl = LeadContactNormalizer.LimitOrNull(SafeAssetUrl(dto.BadgeUrl), 500);
            template.IsEnabled = dto.IsEnabled;
            template.Version++;
            template.UpdatedAt = DateTime.UtcNow;
            template.UpdatedByUserId = ctx.UserId;

            if (isNew)
                _context.NotificationTemplates.Add(template);

            Audit(ctx, "template", isNew ? "create" : "update",
                $"{definition.Name} ({channel}) saved as version {template.Version}.", template.Id);

            await _context.SaveChangesAsync(cancellationToken);
            return await GetTemplateAsync(type, channel, cancellationToken);
        }

        public async Task<NotificationPreviewDto> PreviewTemplateAsync(
            NotificationType type, SaveNotificationTemplateDto? draft, CancellationToken cancellationToken = default)
        {
            var definition = NotificationCatalog.Get(type);
            var sample = BuildSample(type, definition);

            if (draft != null)
            {
                var draftChannel = draft.Channel ?? NotificationChannel.Email;
                var variables = draftChannel == NotificationChannel.WebPush
                    ? NotificationCatalog.PushVariablesFor(type)
                    : NotificationCatalog.VariablesFor(type);
                NotificationTemplateRenderer.Validate(draft.Subject, variables, "Subject");
                NotificationTemplateRenderer.Validate(draft.Heading, variables, "Heading");
                NotificationTemplateRenderer.Validate(draft.Body, variables, "Body");
                NotificationTemplateRenderer.Validate(draft.Footer, variables, "Footer");
            }

            NotificationTemplate? emailDraft = null;
            NotificationTemplate? pushDraft = null;
            if (draft != null)
            {
                if ((draft.Channel ?? NotificationChannel.Email) == NotificationChannel.WebPush)
                    pushDraft = DraftTemplate(type, NotificationChannel.WebPush, definition, draft);
                else
                    emailDraft = DraftTemplate(type, NotificationChannel.Email, definition, draft);
            }

            var email = await _renderer.RenderEmailAsync(sample, "Sample Recipient", cancellationToken, emailDraft);
            var push = await _renderer.RenderPushAsync(sample, cancellationToken, pushDraft);

            return new NotificationPreviewDto
            {
                Subject = email.Subject,
                Html = email.Html,
                PlainText = email.Text,
                PushTitle = push.Title,
                PushBody = push.Body,
                ActionUrl = email.ActionUrl
            };
        }

        /// <summary>Realistic but obviously fake values, so a preview never shows real
        /// customer data to whoever is editing a template.</summary>
        private static Notification BuildSample(NotificationType type, NotificationDefinition definition)
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customerName"] = "Sample Customer",
                ["employeeName"] = "Sample Employee",
                ["managerName"] = "Sample Manager",
                ["leadName"] = "Sample Lead",
                ["leadReference"] = "LD-000123",
                ["authorName"] = "Sample Colleague",
                ["excerpt"] = "…please check the payment plan for this client.",
                ["amount"] = "PKR 250,000.00",
                ["installmentAmount"] = "PKR 125,000.00",
                ["installmentNumber"] = "3",
                ["paymentDate"] = DateTime.UtcNow.ToString("dd MMM yyyy"),
                ["dueDate"] = DateTime.UtcNow.AddDays(7).ToString("dd MMM yyyy"),
                ["visitDate"] = DateTime.UtcNow.AddDays(2).ToString("dd MMM yyyy HH:mm"),
                ["receiptNumber"] = "RCP-000123",
                ["bookingReference"] = "BK-000045",
                ["projectName"] = "Sample Project",
                ["unitNumber"] = "A-101",
                ["paymentMethod"] = "Bank Transfer",
                ["location"] = "Sample Site Office",
                ["stage"] = "Negotiation",
                ["status"] = "Approved",
                ["reason"] = "Sample reason.",
                ["taskTitle"] = "Sample task",
                ["dueDateSuffix"] = $", due {DateTime.UtcNow.AddDays(3):dd MMM yyyy}",
                ["priority"] = "Normal",
                ["updateSummary"] = "Construction has reached the fifth floor.",
                ["title"] = definition.Name,
                ["message"] = "This is a preview of how the message will look."
            };

            return new Notification
            {
                Type = type,
                Category = definition.Category,
                Priority = definition.Priority,
                Title = definition.Name,
                Message = "This is a preview of how the message will look.",
                DeepLink = "/notifications",
                DataJson = System.Text.Json.JsonSerializer.Serialize(data)
            };
        }

        private static NotificationTemplate DraftTemplate(
            NotificationType type,
            NotificationChannel channel,
            NotificationDefinition definition,
            SaveNotificationTemplateDto draft) => new()
        {
            Type = type,
            Channel = channel,
            Category = definition.Category,
            Name = draft.Name ?? definition.Name,
            Subject = draft.Subject,
            Heading = draft.Heading,
            Body = draft.Body,
            ActionText = draft.ActionText,
            ActionUrl = NotificationLink.Sanitize(draft.ActionUrl),
            Footer = draft.Footer,
            IconUrl = SafeAssetUrl(draft.IconUrl),
            BadgeUrl = SafeAssetUrl(draft.BadgeUrl),
            IsEnabled = true
        };

        // ── Rules ───────────────────────────────────────────────────────────────────

        public async Task<List<NotificationRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
        {
            var stored = await _context.NotificationRules.AsNoTracking().ToDictionaryAsync(r => r.Type, cancellationToken);

            return NotificationCatalog.All
                .OrderBy(d => d.Category)
                .ThenBy(d => d.Name)
                .Select(definition =>
                {
                    stored.TryGetValue(definition.Type, out var rule);
                    return MapRule(definition, rule);
                })
                .ToList();
        }

        public async Task<NotificationRuleDto> SaveRuleAsync(
            NotificationType type, SaveNotificationRuleDto dto, NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            NotificationAccess.EnsureAdmin(ctx);

            var definition = NotificationCatalog.Get(type);
            var essential = definition.IsMandatory || NotificationCatalog.IsMandatoryCategory(definition.Category);

            if (essential)
            {
                // Receipts, security messages and critical booking changes carry contractual
                // weight. They can be tuned but never switched off, and weakening one has to
                // be a deliberate, acknowledged act.
                if (!dto.IsEnabled || !dto.InAppEnabled)
                    throw new InvalidOperationException(
                        $"'{definition.Name}' is an essential notification and cannot be switched off.");

                var weakening = (!dto.EmailEnabled && definition.DefaultChannels.HasFlag(NotificationChannel.Email))
                                || (!dto.PushEnabled && definition.DefaultChannels.HasFlag(NotificationChannel.WebPush));

                if (weakening && !dto.ConfirmEssentialChange)
                    throw new InvalidOperationException(
                        $"'{definition.Name}' is essential. Confirm that you want to reduce how it is delivered before saving.");
            }

            if (dto.DelayMinutes is < 0 or > 10_080)
                throw new InvalidOperationException("The delay must be between 0 minutes and 7 days.");

            if (dto.ReminderLeadDays is < 0 or > 60)
                throw new InvalidOperationException("The reminder lead time must be between 0 and 60 days.");

            var rule = await _context.NotificationRules.FirstOrDefaultAsync(r => r.Type == type, cancellationToken);
            var isNew = rule == null;
            rule ??= new NotificationRule { Type = type };

            rule.IsEnabled = dto.IsEnabled;
            rule.InAppEnabled = dto.InAppEnabled;
            rule.EmailEnabled = dto.EmailEnabled;
            rule.PushEnabled = dto.PushEnabled;
            rule.Priority = dto.Priority;
            rule.DelayMinutes = dto.DelayMinutes;
            rule.ReminderLeadDays = dto.ReminderLeadDays;
            rule.RemindOnDueDate = dto.RemindOnDueDate;
            rule.RepeatWhenOverdue = dto.RepeatWhenOverdue;
            rule.EscalateToSupervisors = dto.EscalateToSupervisors;
            rule.UpdatedAt = DateTime.UtcNow;
            rule.UpdatedByUserId = ctx.UserId;

            if (isNew)
                _context.NotificationRules.Add(rule);

            Audit(ctx, "rule", isNew ? "create" : "update",
                $"{definition.Name}: enabled={dto.IsEnabled}, in-app={dto.InAppEnabled}, email={dto.EmailEnabled}, push={dto.PushEnabled}.");

            await _context.SaveChangesAsync(cancellationToken);
            return MapRule(definition, rule);
        }

        public async Task<List<NotificationAuditDto>> GetAuditAsync(int take, CancellationToken cancellationToken = default) =>
            await _context.NotificationAuditEntries
                .AsNoTracking()
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Take(Math.Clamp(take, 1, 200))
                .Select(a => new NotificationAuditDto
                {
                    Id = a.Id,
                    Area = a.Area,
                    Action = a.Action,
                    Details = a.Details,
                    PerformedByName = a.PerformedByName,
                    OccurredAt = a.OccurredAt
                })
                .ToListAsync(cancellationToken);

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private void Audit(NotificationUserContext ctx, string area, string action, string? details, int? entityId = null) =>
            _context.NotificationAuditEntries.Add(new NotificationAuditEntry
            {
                Area = area,
                Action = action,
                Details = LeadContactNormalizer.LimitOrNull(details, 2000),
                EntityId = entityId,
                PerformedByUserId = ctx.UserId,
                PerformedByName = LeadContactNormalizer.LimitOrNull(ctx.DisplayName, 200),
                OccurredAt = DateTime.UtcNow
            });

        private async Task<Dictionary<int, string>> ResolveNamesAsync(IEnumerable<int?> userIds, CancellationToken cancellationToken)
        {
            var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            if (ids.Length == 0)
                return new Dictionary<int, string>();

            return await _context.Users
                .AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        }

        private static NotificationTemplateDto Map(
            NotificationDefinition definition, NotificationChannel channel, NotificationTemplate? template, IReadOnlyDictionary<int, string> editors)
        {
            var mandatory = definition.IsMandatory || NotificationCatalog.IsMandatoryCategory(definition.Category);

            return new NotificationTemplateDto
            {
                Id = template?.Id ?? 0,
                Type = definition.Type,
                Channel = channel,
                Name = template?.Name ?? definition.Name,
                Category = definition.Category,
                Subject = template?.Subject ?? (channel == NotificationChannel.Email ? definition.DefaultSubject : definition.Name),
                Heading = template?.Heading,
                Body = template?.Body ?? (channel == NotificationChannel.WebPush
                    ? "Open DAMS to see the details."
                    : definition.DefaultBody),
                ActionText = template?.ActionText ?? definition.DefaultActionText,
                ActionUrl = template?.ActionUrl,
                Footer = template?.Footer,
                IconUrl = template?.IconUrl,
                BadgeUrl = template?.BadgeUrl,
                IsEnabled = template?.IsEnabled ?? true,
                Version = template?.Version ?? 0,
                UpdatedAt = template?.UpdatedAt,
                UpdatedByName = template?.UpdatedByUserId is { } id && editors.TryGetValue(id, out var name) ? name : null,
                AvailableVariables = (channel == NotificationChannel.WebPush
                    ? NotificationCatalog.PushVariablesFor(definition.Type)
                    : NotificationCatalog.VariablesFor(definition.Type)).ToList(),
                IsMandatory = mandatory
            };
        }

        private static NotificationRuleDto MapRule(NotificationDefinition definition, NotificationRule? rule) =>
            new()
            {
                Type = definition.Type,
                Name = definition.Name,
                Category = definition.Category,
                Module = definition.Module,
                IsEnabled = rule?.IsEnabled ?? true,
                InAppEnabled = rule?.InAppEnabled ?? definition.DefaultChannels.HasFlag(NotificationChannel.InApp),
                EmailEnabled = rule?.EmailEnabled ?? definition.DefaultChannels.HasFlag(NotificationChannel.Email),
                PushEnabled = rule?.PushEnabled ?? definition.DefaultChannels.HasFlag(NotificationChannel.WebPush),
                Priority = rule?.Priority ?? definition.Priority,
                DelayMinutes = rule?.DelayMinutes ?? 0,
                ReminderLeadDays = rule?.ReminderLeadDays ?? DefaultLeadDays(definition.Type),
                RemindOnDueDate = rule?.RemindOnDueDate ?? true,
                RepeatWhenOverdue = rule?.RepeatWhenOverdue ?? definition.Type == NotificationType.InstallmentOverdue,
                EscalateToSupervisors = rule?.EscalateToSupervisors ?? false,
                IsMandatory = definition.IsMandatory || NotificationCatalog.IsMandatoryCategory(definition.Category),
                UpdatedAt = rule?.UpdatedAt
            };

        public static int DefaultLeadDays(NotificationType type) => type switch
        {
            NotificationType.InstallmentDue => 3,
            NotificationType.SiteVisitReminder => 1,
            NotificationType.FollowUpDue => 0,
            _ => 0
        };

        private static void Validate(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            switch (key)
            {
                case NotificationSettingKeys.SupportEmail:
                case NotificationSettingKeys.EmailSenderAddress:
                case NotificationSettingKeys.EmailReplyTo:
                case NotificationSettingKeys.EmailTestRecipient:
                    if (!SmtpEmailSender.IsValidAddress(value))
                        throw new InvalidOperationException($"'{value}' is not a valid email address.");
                    break;

                case NotificationSettingKeys.EmailSmtpPort:
                    if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
                        throw new InvalidOperationException("The SMTP port must be between 1 and 65535.");
                    break;

                case NotificationSettingKeys.EmailProvider:
                    if (!value.Trim().Equals("smtp", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Only the SMTP email provider is currently supported.");
                    break;

                case NotificationSettingKeys.EmailSmtpHost:
                    if (!SmtpEmailSender.IsValidHost(value))
                        throw new InvalidOperationException("Enter an SMTP host without a URL scheme, port or path.");
                    break;

                case NotificationSettingKeys.EmailEnabled:
                case NotificationSettingKeys.EmailSmtpUseSsl:
                case NotificationSettingKeys.EmailAttachReceipt:
                case NotificationSettingKeys.PushEnabled:
                    if (!bool.TryParse(value, out _))
                        throw new InvalidOperationException($"'{key}' must be true or false.");
                    break;

                case NotificationSettingKeys.PublicBaseUrl:
                    if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        throw new InvalidOperationException("The public address must be a full http or https URL.");
                    break;

                case NotificationSettingKeys.PushDefaultUrl:
                    if (NotificationLink.Sanitize(value) == null)
                        throw new InvalidOperationException("The default push destination must be a path inside DAMS.");
                    break;

                case NotificationSettingKeys.PushVapidSubject:
                    if (!PushSubscriptionService.IsValidVapidSubject(value))
                        throw new InvalidOperationException(
                            "The push subject must be a valid mailto: address or an https URL.");
                    break;

                case NotificationSettingKeys.CompanyLogoUrl:
                case NotificationSettingKeys.PushIconUrl:
                case NotificationSettingKeys.PushBadgeUrl:
                    if (SafeAssetUrl(value) == null)
                        throw new InvalidOperationException("An image address must be an https URL or a path inside DAMS.");
                    break;

                case NotificationSettingKeys.EmailHeader:
                case NotificationSettingKeys.EmailFooter:
                    NotificationTemplateRenderer.Validate(value, NotificationCatalog.CommonVariables, key);
                    break;
            }
        }

        private static void ValidateEmailConfiguration(IReadOnlyDictionary<string, string?> values)
        {
            string? Value(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;

            var enabled = bool.TryParse(Value(NotificationSettingKeys.EmailEnabled), out var parsed) && parsed;
            var username = Value(NotificationSettingKeys.EmailSmtpUsername);
            var password = Value(NotificationSettingKeys.EmailSmtpPassword);
            if ((username == null) != (password == null))
                throw new InvalidOperationException("SMTP username and password must either both be set or both be empty.");

            var useTls = !bool.TryParse(Value(NotificationSettingKeys.EmailSmtpUseSsl), out var tls) || tls;
            if (!useTls && username != null)
                throw new InvalidOperationException(SmtpEmailSender.PlaintextCredentialsError);

            if (!enabled)
                return;

            if (!string.Equals(Value(NotificationSettingKeys.EmailProvider) ?? "smtp", "smtp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only the SMTP email provider is currently supported.");
            if (!SmtpEmailSender.IsValidHost(Value(NotificationSettingKeys.EmailSmtpHost)))
                throw new InvalidOperationException("A valid SMTP host is required when email is enabled.");
            if (!SmtpEmailSender.IsValidAddress(Value(NotificationSettingKeys.EmailSenderAddress)))
                throw new InvalidOperationException("A valid sender address is required when email is enabled.");
        }

        /// <summary>Images may only come from DAMS itself or an https host — never from a
        /// javascript: or data: URL that would run inside a mail client.</summary>
        private static string? SafeAssetUrl(string? value)
        {
            var cleaned = LeadContactNormalizer.Clean(value);
            if (cleaned == null)
                return null;

            if (NotificationLink.Sanitize(cleaned) != null)
                return cleaned;

            return Uri.TryCreate(cleaned, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.AbsoluteUri
                : null;
        }

        private static string Mask(string value) =>
            value.Length <= 4 ? SecretPlaceholder : $"{SecretPlaceholder}{value[^4..]}";

        private static DateTime? ParseDate(string? value) =>
            DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }
}
