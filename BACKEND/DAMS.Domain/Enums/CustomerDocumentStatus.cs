namespace DAMS.Domain.Enums
{
    public enum CustomerDocumentStatus
    {
        Missing = 0,
        Requested = 1,
        Received = 2,
        UnderReview = 3,
        Approved = 4,
        Rejected = 5,
        ReplacementRequired = 6,
        Postponed = 7,
        Waived = 8,
        NotApplicable = 9,
        Expired = 10
    }

    public enum CustomerDocumentVersionStatus
    {
        UnderReview = 0,
        Approved = 1,
        Rejected = 2,
        ReplacementRequired = 3,
        Superseded = 4
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
        FileDownloaded = 19
    }

    public enum CustomerDocumentAssignmentMode
    {
        None = 0,
        NewCustomersOnly = 1,
        AllActiveCustomers = 2,
        SelectedCustomers = 3
    }
}
