using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// The admin's switchboard for one notification type. A missing row means "use the
    /// catalog defaults", so the platform behaves correctly on a database that has never had
    /// its rules touched.
    /// </summary>
    public class NotificationRule
    {
        public int Id { get; set; }

        public NotificationType Type { get; set; }

        public bool IsEnabled { get; set; } = true;

        public bool InAppEnabled { get; set; } = true;

        public bool EmailEnabled { get; set; }

        public bool PushEnabled { get; set; }

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        /// <summary>Minutes to hold a notification back before its first send attempt.</summary>
        public int DelayMinutes { get; set; }

        /// <summary>How many days before a dated event (installment due, site visit) the
        /// reminder goes out. Ignored by types that are not time-based.</summary>
        public int ReminderLeadDays { get; set; }

        /// <summary>Also remind on the day itself.</summary>
        public bool RemindOnDueDate { get; set; } = true;

        /// <summary>Keep reminding after the due date until the item is dealt with.</summary>
        public bool RepeatWhenOverdue { get; set; }

        /// <summary>Copy supervisors when the event goes unanswered.</summary>
        public bool EscalateToSupervisors { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public int? UpdatedByUserId { get; set; }
    }
}
