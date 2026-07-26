using DAMS.Domain.Enums;

namespace DAMS.Domain.Entities
{
    /// <summary>
    /// One user's choice for one category. Only the optional channels are user-controlled;
    /// the in-app inbox stays available so nobody can mute themselves out of a record they
    /// are accountable for. Mandatory categories ignore these rows entirely.
    /// </summary>
    public class NotificationPreference
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public NotificationCategory Category { get; set; }

        public bool EmailEnabled { get; set; } = true;

        public bool PushEnabled { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;
    }
}
