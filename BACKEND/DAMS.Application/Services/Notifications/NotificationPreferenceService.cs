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
    /// Per-category channel preferences. The list is always complete — a user with no stored
    /// rows still sees every category with its default — and mandatory categories are
    /// returned flagged so the interface can show them without offering a switch.
    /// </summary>
    public sealed class NotificationPreferenceService : INotificationPreferenceService
    {
        private readonly AppDbContext _context;
        private readonly NotificationSettingsStore _settings;

        public NotificationPreferenceService(AppDbContext context, NotificationSettingsStore settings)
        {
            _context = context;
            _settings = settings;
        }

        private static readonly (NotificationCategory Category, string Label, string Description)[] Descriptions =
        {
            (NotificationCategory.PaymentsAndReceipts, "Payments and receipts", "Confirmation and official receipt whenever a payment is recorded."),
            (NotificationCategory.BookingUpdates, "Booking updates", "Approvals, rejections, cancellations and possession."),
            (NotificationCategory.InstallmentReminders, "Installment reminders", "Reminders before and after an installment falls due."),
            (NotificationCategory.LeadAssignments, "Lead assignments", "Leads assigned to you or moved between owners."),
            (NotificationCategory.FollowUps, "Follow-ups", "Follow-up work that is due or overdue."),
            (NotificationCategory.SiteVisits, "Site visits", "Visits booked, changed or coming up."),
            (NotificationCategory.Mentions, "Mentions", "Someone mentions you in an internal comment."),
            (NotificationCategory.EmployeeTasks, "Employee tasks", "Tasks assigned to you."),
            (NotificationCategory.ProjectUpdates, "Project updates", "Progress and news on projects you follow."),
            (NotificationCategory.Announcements, "Announcements", "General messages from the DAMS team."),
            (NotificationCategory.AccountAndSecurity, "Account and security", "Sign-in, password and account safety messages."),
            (NotificationCategory.ManagerEscalations, "Escalations", "Team issues that need a supervisor's attention.")
        };

        public async Task<List<NotificationPreferenceDto>> GetAsync(
            NotificationUserContext ctx, CancellationToken cancellationToken = default)
        {
            var stored = await _context.NotificationPreferences
                .AsNoTracking()
                .Where(p => p.UserId == ctx.UserId)
                .ToDictionaryAsync(p => p.Category, cancellationToken);

            var emailOn = await _settings.GetBoolAsync(NotificationSettingKeys.EmailEnabled, false, cancellationToken);
            var pushOn = await _settings.GetBoolAsync(NotificationSettingKeys.PushEnabled, false, cancellationToken);

            // A category is only offered when at least one notification in it can actually
            // reach this person on that channel.
            var emailCategories = CategoriesSupporting(NotificationChannel.Email);
            var pushCategories = CategoriesSupporting(NotificationChannel.WebPush);

            return Descriptions
                .Where(d => ctx.IsStaff || IsCustomerFacing(d.Category))
                .Select(d =>
                {
                    var mandatory = NotificationCatalog.IsMandatoryCategory(d.Category);
                    stored.TryGetValue(d.Category, out var row);

                    return new NotificationPreferenceDto
                    {
                        Category = d.Category,
                        Label = d.Label,
                        Description = d.Description,
                        IsMandatory = mandatory,
                        EmailEnabled = mandatory || (row?.EmailEnabled ?? true),
                        PushEnabled = mandatory || (row?.PushEnabled ?? true),
                        EmailAvailable = emailOn && emailCategories.Contains(d.Category),
                        PushAvailable = pushOn && pushCategories.Contains(d.Category)
                    };
                })
                .ToList();
        }

        public async Task<List<NotificationPreferenceDto>> UpdateAsync(
            NotificationUserContext ctx, UpdateNotificationPreferencesDto dto, CancellationToken cancellationToken = default)
        {
            var existing = await _context.NotificationPreferences
                .Where(p => p.UserId == ctx.UserId)
                .ToDictionaryAsync(p => p.Category, cancellationToken);

            foreach (var item in dto.Items.DistinctBy(i => i.Category))
            {
                // Silently ignoring a mandatory category is the right answer: the interface
                // never offers it, and a crafted request must not be able to switch off a
                // receipt or a security message.
                if (NotificationCatalog.IsMandatoryCategory(item.Category))
                    continue;

                if (existing.TryGetValue(item.Category, out var row))
                {
                    row.EmailEnabled = item.EmailEnabled;
                    row.PushEnabled = item.PushEnabled;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.NotificationPreferences.Add(new NotificationPreference
                    {
                        UserId = ctx.UserId,
                        Category = item.Category,
                        EmailEnabled = item.EmailEnabled,
                        PushEnabled = item.PushEnabled,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return await GetAsync(ctx, cancellationToken);
        }

        private static HashSet<NotificationCategory> CategoriesSupporting(NotificationChannel channel) =>
            NotificationCatalog.All
                .Where(d => d.DefaultChannels.HasFlag(channel))
                .Select(d => d.Category)
                .ToHashSet();

        /// <summary>Categories a customer can meaningfully receive. Internal CRM traffic is
        /// never offered to them, so their preference screen cannot hint at its existence.</summary>
        private static bool IsCustomerFacing(NotificationCategory category) =>
            category is NotificationCategory.PaymentsAndReceipts
                     or NotificationCategory.BookingUpdates
                     or NotificationCategory.InstallmentReminders
                     or NotificationCategory.ProjectUpdates
                     or NotificationCategory.Announcements
                     or NotificationCategory.AccountAndSecurity;
    }
}
