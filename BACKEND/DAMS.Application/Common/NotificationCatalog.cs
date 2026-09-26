using DAMS.Domain.Enums;

namespace DAMS.Application.Common
{
    /// <summary>
    /// Everything the platform knows about a notification type before an admin touches it:
    /// which category it belongs to, which channels it uses out of the box, whether a user
    /// may switch it off, and the wording to fall back on when no template exists.
    ///
    /// This is the single source of truth that keeps a fresh database, a half-configured one
    /// and a fully customised one all behaving sensibly.
    /// </summary>
    public sealed record NotificationDefinition(
        NotificationType Type,
        NotificationCategory Category,
        NotificationModule Module,
        NotificationPriority Priority,
        NotificationChannel DefaultChannels,
        bool IsMandatory,
        string Name,
        string DefaultSubject,
        string DefaultBody,
        string? DefaultActionText,
        IReadOnlyList<string> Variables);

    public static class NotificationCatalog
    {
        /// <summary>Variables every template may use, on top of its own.</summary>
        public static readonly string[] CommonVariables =
        {
            "companyName", "appName", "supportEmail", "supportPhone", "recipientName", "actionUrl", "year"
        };

        private static readonly Dictionary<NotificationType, NotificationDefinition> Definitions =
            Build().ToDictionary(d => d.Type);

        public static IReadOnlyCollection<NotificationDefinition> All => Definitions.Values;

        public static bool TryGet(NotificationType type, out NotificationDefinition definition) =>
            Definitions.TryGetValue(type, out definition!);

        public static NotificationDefinition GetRequired(NotificationType type) =>
            TryGet(type, out var definition)
                ? definition
                : throw new InvalidOperationException($"Notification type '{type}' is not registered in the catalog.");

        public static NotificationDefinition Get(NotificationType type) => GetRequired(type);

        public static NotificationCategory CategoryOf(NotificationType type) => Get(type).Category;

        /// <summary>
        /// Categories a user may never mute. Receipts, security messages and critical booking
        /// changes are contractual or legal, so preference rows for them are ignored.
        /// </summary>
        public static bool IsMandatoryCategory(NotificationCategory category) =>
            category is NotificationCategory.PaymentsAndReceipts
                     or NotificationCategory.AccountAndSecurity;

        public static bool IsMandatory(NotificationType type) => Get(type).IsMandatory;

        /// <summary>Approved variables for a template on this type — anything else is rejected.</summary>
        public static IReadOnlyList<string> VariablesFor(NotificationType type) =>
            CommonVariables.Concat(Get(type).Variables).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Lock-screen copy has a deliberately smaller variable surface than email. Business
        /// identifiers, names, amounts and free-form CRM fields are never template variables
        /// for browser push.
        /// </summary>
        public static IReadOnlyList<string> PushVariablesFor(NotificationType type)
        {
            var safe = new List<string> { "companyName", "appName", "actionUrl", "year" };
            if (type is NotificationType.AdminAnnouncement
                or NotificationType.AccountSecurity
                or NotificationType.ProjectUpdated)
            {
                safe.Add("title");
                safe.Add("message");
            }
            return safe;
        }

        private static IEnumerable<NotificationDefinition> Build()
        {
            const NotificationChannel all = NotificationChannel.InApp | NotificationChannel.Email | NotificationChannel.WebPush;
            const NotificationChannel appPush = NotificationChannel.InApp | NotificationChannel.WebPush;
            const NotificationChannel appOnly = NotificationChannel.InApp;

            string[] payment = { "customerName", "amount", "paymentDate", "receiptNumber", "bookingReference", "projectName", "unitNumber", "paymentMethod", "receiptUrl" };
            string[] booking = { "customerName", "bookingReference", "projectName", "unitNumber", "status", "reason" };
            string[] installment = { "customerName", "installmentAmount", "dueDate", "bookingReference", "projectName", "unitNumber", "installmentNumber" };
            string[] lead = { "leadName", "leadReference", "employeeName", "managerName", "stage", "reason" };
            string[] heldEnquiry = { "enquiryName", "holdReference", "sourceName" };
            string[] followUp = { "leadName", "leadReference", "employeeName", "followUpTitle", "dueDate" };
            string[] visit = { "leadName", "leadReference", "employeeName", "visitDate", "location", "projectName" };

            yield return new(NotificationType.PaymentReceipt, NotificationCategory.PaymentsAndReceipts, NotificationModule.Payments,
                NotificationPriority.High, all, true, "Payment Receipt",
                "Payment received — receipt {{receiptNumber}}",
                "Dear {{customerName}}, we have received your payment of {{amount}} on {{paymentDate}} against booking {{bookingReference}}. Your official receipt {{receiptNumber}} is attached.",
                "View receipt", payment);

            yield return new(NotificationType.BookingRequestReceived, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.Normal, all, false, "Property Inquiry Received",
                "We have received your inquiry",
                "Dear {{customerName}}, thank you for your interest in {{projectName}} ({{unitNumber}}). Our team will contact you shortly.",
                "View request", booking);

            yield return new(NotificationType.BookingApproved, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.High, all, true, "Booking Approved",
                "Your booking {{bookingReference}} is approved",
                "Dear {{customerName}}, your booking for {{unitNumber}} at {{projectName}} has been approved. Reference {{bookingReference}}.",
                "View booking", booking);

            yield return new(NotificationType.BookingRejected, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.High, all, true, "Booking Rejected",
                "Update on your request for {{unitNumber}}",
                "Dear {{customerName}}, your request for {{unitNumber}} at {{projectName}} could not be approved. {{reason}}",
                "Open DAMS", booking);

            yield return new(NotificationType.BookingCancelled, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.High, all, true, "Booking Cancelled",
                "Booking {{bookingReference}} has been cancelled",
                "Dear {{customerName}}, booking {{bookingReference}} for {{unitNumber}} at {{projectName}} has been cancelled. {{reason}}",
                "View booking", booking);

            yield return new(NotificationType.PossessionGiven, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.Normal, all, false, "Possession Handed Over",
                "Possession handed over for {{unitNumber}}",
                "Dear {{customerName}}, possession of {{unitNumber}} at {{projectName}} has been handed over. Reference {{bookingReference}}.",
                "View booking", booking);

            yield return new(NotificationType.SaleCompleted, NotificationCategory.BookingUpdates, NotificationModule.Bookings,
                NotificationPriority.Normal, all, false, "Sale Completed",
                "Sale completed for {{unitNumber}}",
                "Dear {{customerName}}, your purchase of {{unitNumber}} at {{projectName}} is now complete. Reference {{bookingReference}}.",
                "View booking", booking);

            yield return new(NotificationType.InstallmentDue, NotificationCategory.InstallmentReminders, NotificationModule.Payments,
                NotificationPriority.Normal, all, false, "Installment Due Reminder",
                "Installment of {{installmentAmount}} due on {{dueDate}}",
                "Dear {{customerName}}, installment {{installmentNumber}} of {{installmentAmount}} for booking {{bookingReference}} is due on {{dueDate}}.",
                "View booking", installment);

            yield return new(NotificationType.InstallmentOverdue, NotificationCategory.InstallmentReminders, NotificationModule.Payments,
                NotificationPriority.High, all, false, "Installment Overdue",
                "Installment of {{installmentAmount}} is overdue",
                "Dear {{customerName}}, installment {{installmentNumber}} of {{installmentAmount}} for booking {{bookingReference}} was due on {{dueDate}} and is still outstanding.",
                "View booking", installment);

            yield return new(NotificationType.LeadCreated, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "New Lead Received",
                "New lead: {{leadName}}",
                "A new lead {{leadName}} ({{leadReference}}) has arrived and is waiting to be assigned.",
                "Open lead", lead);

            yield return new(NotificationType.LeadAssigned, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.High, all, false, "Lead Assigned",
                "You have been assigned {{leadName}}",
                "Hello {{employeeName}}, lead {{leadName}} ({{leadReference}}) is now yours. Please make first contact as soon as possible.",
                "Open lead", lead);

            yield return new(NotificationType.LeadReassigned, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Normal, all, false, "Lead Reassigned",
                "{{leadName}} has been reassigned",
                "Hello {{employeeName}}, ownership of lead {{leadName}} ({{leadReference}}) has changed. {{reason}}",
                "Open lead", lead);

            yield return new(NotificationType.LeadStageChanged, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Low, appOnly, false, "Lead Stage Changed",
                "{{leadName}} moved to {{stage}}",
                "Lead {{leadName}} ({{leadReference}}) has moved to {{stage}}.",
                "Open lead", lead);

            yield return new(NotificationType.LeadConverted, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "Lead Converted",
                "{{leadName}} converted to a booking",
                "Lead {{leadName}} ({{leadReference}}) has been converted into a booking.",
                "Open lead", lead);

            yield return new(NotificationType.LeadClosed, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Low, appOnly, false, "Lead Closed",
                "{{leadName}} was closed",
                "Lead {{leadName}} ({{leadReference}}) has been closed. {{reason}}",
                "Open lead", lead);

            yield return new(NotificationType.LeadInactive, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "Lead Inactive",
                "No activity on {{leadName}}",
                "Lead {{leadName}} ({{leadReference}}) has had no recorded activity and needs attention.",
                "Open lead", lead);

            yield return new(NotificationType.FirstContactDue, NotificationCategory.FollowUps, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "First Contact Due",
                "First contact due: {{leadName}}",
                "Hello {{employeeName}}, lead {{leadName}} ({{leadReference}}) is still waiting for a first conversation.",
                "Open lead", lead);

            yield return new(NotificationType.FirstContactOverdue, NotificationCategory.FollowUps, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "First Contact Overdue",
                "First contact overdue: {{leadName}}",
                "Hello {{employeeName}}, lead {{leadName}} ({{leadReference}}) has passed its first-response target with no contact recorded.",
                "Open lead", lead);

            yield return new(NotificationType.LeadHeldForReview, NotificationCategory.LeadAssignments, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Meta Enquiry Held for Review",
                "Meta enquiry {{holdReference}} needs review",
                "{{enquiryName}} from {{sourceName}} matches multiple leads. Choose the correct lead in the Leads page.",
                "Review enquiry", heldEnquiry);

            yield return new(NotificationType.FollowUpAssigned, NotificationCategory.FollowUps, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "Follow-up Assigned",
                "New follow-up on {{leadName}}",
                "Hello {{employeeName}}, {{followUpTitle}} on lead {{leadName}} is due {{dueDate}}.",
                "Open lead", followUp);

            yield return new(NotificationType.FollowUpDue, NotificationCategory.FollowUps, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "Follow-up Due",
                "Follow-up due: {{leadName}}",
                "Hello {{employeeName}}, {{followUpTitle}} on lead {{leadName}} is due {{dueDate}}.",
                "Open lead", followUp);

            yield return new(NotificationType.FollowUpOverdue, NotificationCategory.FollowUps, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Follow-up Overdue",
                "Follow-up overdue: {{leadName}}",
                "Hello {{employeeName}}, {{followUpTitle}} on lead {{leadName}} was due {{dueDate}} and is still open.",
                "Open lead", followUp);

            yield return new(NotificationType.FollowUpMissed, NotificationCategory.ManagerEscalations, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Follow-up Missed",
                "Follow-up missed on {{leadName}}",
                "{{followUpTitle}} on lead {{leadName}} was due {{dueDate}} and was never actioned.",
                "Open lead", followUp);

            yield return new(NotificationType.SiteVisitScheduled, NotificationCategory.SiteVisits, NotificationModule.Leads,
                NotificationPriority.Normal, all, false, "Site Visit Scheduled",
                "Site visit scheduled for {{leadName}}",
                "A site visit for {{leadName}} is scheduled on {{visitDate}} at {{location}}.",
                "Open lead", visit);

            yield return new(NotificationType.SiteVisitUpdated, NotificationCategory.SiteVisits, NotificationModule.Leads,
                NotificationPriority.Normal, all, false, "Site Visit Updated",
                "Site visit for {{leadName}} has changed",
                "The site visit for {{leadName}} has moved to {{visitDate}} at {{location}}.",
                "Open lead", visit);

            yield return new(NotificationType.SiteVisitReminder, NotificationCategory.SiteVisits, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Site Visit Reminder",
                "Site visit reminder: {{leadName}}",
                "Hello {{employeeName}}, your site visit for {{leadName}} is at {{visitDate}}, {{location}}.",
                "Open lead", visit);

            yield return new(NotificationType.SiteVisitMissed, NotificationCategory.ManagerEscalations, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Site Visit Missed",
                "Site visit missed: {{leadName}}",
                "The site visit for {{leadName}} scheduled on {{visitDate}} has no recorded outcome.",
                "Open lead", visit);

            yield return new(NotificationType.UserMentioned, NotificationCategory.Mentions, NotificationModule.Leads,
                NotificationPriority.Normal, appPush, false, "User Mentioned",
                "{{authorName}} mentioned you",
                "{{authorName}} mentioned you in a comment on {{leadName}}.",
                "Open lead", new[] { "leadName", "leadReference", "authorName", "excerpt" });

            yield return new(NotificationType.EmployeeTaskAssigned, NotificationCategory.EmployeeTasks, NotificationModule.Employees,
                NotificationPriority.Normal, all, false, "Employee Task Assigned",
                "New task: {{taskTitle}}",
                "Hello {{employeeName}}, a new task \"{{taskTitle}}\" has been assigned to you{{dueDateSuffix}}.",
                "Open task", new[] { "employeeName", "taskTitle", "dueDate", "dueDateSuffix", "priority", "projectName" });

            yield return new(NotificationType.ProjectUpdated, NotificationCategory.ProjectUpdates, NotificationModule.Projects,
                NotificationPriority.Low, all, false, "Project Update",
                "Update on {{projectName}}",
                "There is an update on {{projectName}}. {{updateSummary}}",
                "View project", new[] { "projectName", "updateSummary", "status" });

            yield return new(NotificationType.AdminAnnouncement, NotificationCategory.Announcements, NotificationModule.System,
                NotificationPriority.Normal, all, false, "General Admin Announcement",
                "{{title}}",
                "{{message}}",
                "Open DAMS", new[] { "title", "message" });

            yield return new(NotificationType.AccountSecurity, NotificationCategory.AccountAndSecurity, NotificationModule.System,
                NotificationPriority.Critical, all, true, "Important Account or Security Message",
                "{{title}}",
                "{{message}}",
                "Open DAMS", new[] { "title", "message" });

            yield return new(NotificationType.ManagerAttentionRequired, NotificationCategory.ManagerEscalations, NotificationModule.Leads,
                NotificationPriority.High, appPush, false, "Manager Attention Required",
                "{{title}}",
                "{{message}}",
                "Open lead", new[] { "title", "message", "leadName", "leadReference", "employeeName" });
        }
    }
}
