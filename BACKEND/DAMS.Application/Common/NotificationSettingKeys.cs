namespace DAMS.Application.Common
{
    /// <summary>
    /// The keys used in the <c>NotificationSettings</c> store. Secrets are listed separately
    /// so the read path can mask them without every caller having to remember which is which.
    /// </summary>
    public static class NotificationSettingKeys
    {
        // General / branding
        public const string AppName = "general.appName";
        public const string CompanyName = "general.companyName";
        public const string CompanyLogoUrl = "general.companyLogoUrl";
        public const string SupportEmail = "general.supportEmail";
        public const string SupportPhone = "general.supportPhone";
        public const string CompanyAddress = "general.companyAddress";
        public const string EmailHeader = "general.emailHeader";
        public const string EmailFooter = "general.emailFooter";
        public const string BrandPrimaryColor = "general.brandPrimaryColor";
        public const string BrandAccentColor = "general.brandAccentColor";
        public const string CopyrightText = "general.copyrightText";
        public const string SocialLinks = "general.socialLinks";
        public const string DateFormat = "general.dateFormat";
        public const string CurrencySymbol = "general.currencySymbol";
        /// <summary>Absolute origin used to turn site-relative deep links into clickable
        /// links in email and push. Must be an allow-listed origin.</summary>
        public const string PublicBaseUrl = "general.publicBaseUrl";

        // Email
        public const string EmailEnabled = "email.enabled";
        public const string EmailProvider = "email.provider";
        public const string EmailSenderName = "email.senderName";
        public const string EmailSenderAddress = "email.senderAddress";
        public const string EmailReplyTo = "email.replyTo";
        public const string EmailSmtpHost = "email.smtp.host";
        public const string EmailSmtpPort = "email.smtp.port";
        public const string EmailSmtpUseSsl = "email.smtp.useSsl";
        public const string EmailSmtpUsername = "email.smtp.username";
        public const string EmailSmtpPassword = "email.smtp.password";
        public const string EmailTestRecipient = "email.testRecipient";
        public const string EmailLastTestAt = "email.lastTestAt";
        public const string EmailLastFailure = "email.lastFailure";
        public const string EmailAttachReceipt = "email.attachReceipt";

        // Browser push
        public const string PushEnabled = "push.enabled";
        public const string PushDisplayName = "push.displayName";
        public const string PushIconUrl = "push.iconUrl";
        public const string PushBadgeUrl = "push.badgeUrl";
        public const string PushDefaultUrl = "push.defaultUrl";
        public const string PushVapidPublicKey = "push.vapid.publicKey";
        public const string PushVapidPrivateKey = "push.vapid.privateKey";
        public const string PushVapidSubject = "push.vapid.subject";
        public const string PushLastTestAt = "push.lastTestAt";
        public const string PushLastFailure = "push.lastFailure";

        // Server-managed migration watermark used by reconciliation. Never client-writable.
        public const string PlatformActivatedAt = "system.platformActivatedAt";

        /// <summary>Values that must never leave the backend in readable form.</summary>
        public static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase)
        {
            EmailSmtpPassword,
            PushVapidPrivateKey
        };

        public static bool IsSecret(string key) => Secrets.Contains(key);

        /// <summary>Keys an admin may write. Anything else is refused, so a crafted request
        /// cannot inject arbitrary configuration.</summary>
        public static readonly HashSet<string> Writable = new(StringComparer.OrdinalIgnoreCase)
        {
            AppName, CompanyName, CompanyLogoUrl, SupportEmail, SupportPhone, CompanyAddress,
            EmailHeader, EmailFooter, BrandPrimaryColor, BrandAccentColor, CopyrightText,
            SocialLinks, DateFormat, CurrencySymbol, PublicBaseUrl,
            EmailEnabled, EmailProvider, EmailSenderName, EmailSenderAddress, EmailReplyTo,
            EmailSmtpHost, EmailSmtpPort, EmailSmtpUseSsl, EmailSmtpUsername, EmailSmtpPassword,
            EmailTestRecipient, EmailAttachReceipt,
            PushEnabled, PushDisplayName, PushIconUrl, PushBadgeUrl, PushDefaultUrl, PushVapidSubject
        };
    }
}
