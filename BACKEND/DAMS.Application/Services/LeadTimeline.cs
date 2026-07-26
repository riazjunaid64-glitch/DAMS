using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The only way lead history is written. Every meaningful action calls this, which both
    /// appends an immutable timeline row and refreshes the lead's "last activity" summary in
    /// one place so the two can never drift apart.
    /// </summary>
    internal static class LeadTimeline
    {
        public static LeadActivity Record(
            AppDbContext context,
            Lead lead,
            LeadActivityType type,
            string summary,
            LeadUserContext? actor,
            Action<LeadActivity>? configure = null)
        {
            var activity = new LeadActivity
            {
                Lead = lead,
                LeadId = lead.Id,
                Type = type,
                Summary = LeadContactNormalizer.Limit(summary, 300),
                PerformedByUserId = actor?.UserId,
                PerformedByName = actor?.DisplayName,
                IsSystemGenerated = actor == null,
                OccurredAt = DateTime.UtcNow
            };

            configure?.Invoke(activity);
            activity.Summary = LeadContactNormalizer.Limit(activity.Summary, 300);
            activity.Notes = LeadContactNormalizer.LimitOrNull(activity.Notes, 2000);
            activity.PreviousValue = LeadContactNormalizer.LimitOrNull(activity.PreviousValue, 300);
            activity.NewValue = LeadContactNormalizer.LimitOrNull(activity.NewValue, 300);

            context.LeadActivities.Add(activity);

            lead.LastActivityAt = activity.OccurredAt;
            lead.LastActivitySummary = activity.Summary;

            return activity;
        }
    }
}
