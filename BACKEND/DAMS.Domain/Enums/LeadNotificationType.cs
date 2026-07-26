namespace DAMS.Domain.Enums
{
    public enum LeadNotificationType
    {
        NewLeadReceived = 0,
        LeadAssigned = 1,
        LeadReassigned = 2,
        FirstContactDue = 3,
        FirstContactOverdue = 4,
        FollowUpDue = 5,
        FollowUpOverdue = 6,
        LeadInactive = 7,
        SiteVisitToday = 8,
        SiteVisitMissed = 9,
        StageChanged = 10,
        ManagerAttentionRequired = 11,
        LeadConverted = 12,
        LeadClosed = 13,
        MentionedInComment = 14,
        TaskAssigned = 15
    }
}
