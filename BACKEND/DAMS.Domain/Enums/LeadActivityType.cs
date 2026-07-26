namespace DAMS.Domain.Enums
{
    /// <summary>
    /// Append-only timeline event types. Values are never reused or renumbered because
    /// historical rows keep the value that was written at the time.
    /// </summary>
    public enum LeadActivityType
    {
        LeadCreated = 0,
        SourceRecorded = 1,
        LeadEnriched = 2,
        LeadAssigned = 3,
        LeadReassigned = 4,
        LeadUnassigned = 5,
        ContactAttempt = 6,
        CallCompleted = 7,
        WhatsappActivity = 8,
        EmailActivity = 9,
        SmsActivity = 10,
        MeetingRecorded = 11,
        OtherCommunication = 12,
        InternalNote = 13,
        InternalComment = 14,
        TeamMemberMentioned = 15,
        TaskCreated = 16,
        FollowUpScheduled = 17,
        FollowUpCompleted = 18,
        FollowUpMissed = 19,
        StageChanged = 20,
        QualificationChanged = 21,
        SiteVisitScheduled = 22,
        SiteVisitRescheduled = 23,
        SiteVisitCompleted = 24,
        SiteVisitMissed = 25,
        SiteVisitCancelled = 26,
        DocumentUploaded = 27,
        DocumentRemoved = 28,
        NegotiationUpdate = 29,
        LeadLost = 30,
        LeadDormant = 31,
        LeadReopened = 32,
        LeadConverted = 33,
        CustomerLinked = 34,
        BookingCreated = 35,
        SystemAlert = 36,
        DetailsUpdated = 37,
        FollowUpRescheduled = 38
    }
}
