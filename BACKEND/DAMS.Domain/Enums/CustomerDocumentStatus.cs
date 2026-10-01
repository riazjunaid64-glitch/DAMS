namespace DAMS.Domain.Enums
{
    public enum CustomerDocumentStatus
    {
        Needed = 0,
        Uploaded = 1,
        NotNeeded = 2
    }

    public enum CustomerDocumentAction
    {
        RequirementCreated = 0,
        CategoryAssigned = 1,
        FileUploaded = 2,
        ReplacementUploaded = 3,
        Approved = 4,
        Rejected = 5,
        ReplacementRequested = 6,
        Requested = 7,
        Postponed = 8,
        Waived = 9,
        MarkedNotApplicable = 10,
        MarkedExpired = 11,
        DueDateChanged = 12,
        CategoryCreated = 13,
        CategoryUpdated = 14,
        CategoryDeactivated = 15,
        CategoryActivated = 16,
        CategoryDeleted = 17,
        BulkCategoryAssignment = 18,
        FileDownloaded = 19,
        FileViewed = 20,
        MarkedNotNeeded = 21,
        DocumentAdded = 22
    }
}
