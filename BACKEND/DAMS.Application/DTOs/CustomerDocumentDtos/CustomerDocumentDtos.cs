using DAMS.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace DAMS.Application.DTOs.CustomerDocumentDtos
{
    public class CustomerDocumentCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsRequiredByDefault { get; set; }
        public int DisplayOrder { get; set; }
        public List<string> AllowedFileTypes { get; set; } = [];
        public long MaxFileSizeBytes { get; set; }
        public bool IsActive { get; set; }
        public bool AssignToNewCustomers { get; set; }
        public int? DefaultDueDays { get; set; }
        public int UsageCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class CreateCustomerDocumentCategoryDto
    {
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        [Required, MaxLength(80)] public string Code { get; set; } = string.Empty;
        [MaxLength(1000)] public string? Description { get; set; }
        public bool IsRequiredByDefault { get; set; }
        [Range(0, 100000)] public int DisplayOrder { get; set; } = 100;
        [MinLength(1)] public List<string> AllowedFileTypes { get; set; } = [".pdf", ".jpg", ".jpeg", ".png"];
        [Range(1, 25 * 1024 * 1024)] public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
        [Range(1, 3650)] public int? DefaultDueDays { get; set; }
        public CustomerDocumentAssignmentMode AssignmentMode { get; set; }
        public List<int> SelectedCustomerIds { get; set; } = [];
    }

    public class UpdateCustomerDocumentCategoryDto
    {
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        [Required, MaxLength(80)] public string Code { get; set; } = string.Empty;
        [MaxLength(1000)] public string? Description { get; set; }
        public bool IsRequiredByDefault { get; set; }
        [Range(0, 100000)] public int DisplayOrder { get; set; }
        [MinLength(1)] public List<string> AllowedFileTypes { get; set; } = [];
        [Range(1, 25 * 1024 * 1024)] public long MaxFileSizeBytes { get; set; }
        [Range(1, 3650)] public int? DefaultDueDays { get; set; }
        public bool IsActive { get; set; }
        public bool AssignToNewCustomers { get; set; }
        [Required] public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class AssignCustomerDocumentCategoryDto
    {
        public CustomerDocumentAssignmentMode AssignmentMode { get; set; }
        public List<int> SelectedCustomerIds { get; set; } = [];
    }

    public class CustomerDocumentAssignmentResultDto
    {
        public int EligibleCustomers { get; set; }
        public int AssignedCustomers { get; set; }
        public int AlreadyAssignedCustomers { get; set; }
    }

    public class AddCustomerDocumentRequirementDto
    {
        public int? CategoryId { get; set; }
        [MaxLength(150)] public string? Name { get; set; }
        [MaxLength(1000)] public string? Description { get; set; }
        public bool IsRequired { get; set; } = true;
        public DateTime? DueDate { get; set; }
        public bool SaveAsGlobalCategory { get; set; }
        [MaxLength(80)] public string? GlobalCategoryCode { get; set; }
        public List<string> AllowedFileTypes { get; set; } = [".pdf", ".jpg", ".jpeg", ".png"];
        [Range(1, 25 * 1024 * 1024)] public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
        public CustomerDocumentAssignmentMode GlobalAssignmentMode { get; set; }
        public List<int> SelectedCustomerIds { get; set; } = [];
    }

    public class CustomerDocumentStatusChangeDto
    {
        public CustomerDocumentStatus Status { get; set; }
        [MaxLength(2000)] public string? Reason { get; set; }
        public DateTime? PostponedUntil { get; set; }
        [Required] public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class CustomerDocumentDueDateDto
    {
        public DateTime? DueDate { get; set; }
        [MaxLength(2000)] public string? Reason { get; set; }
        [Required] public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class CustomerDocumentSummaryDto
    {
        public int RequiredTotal { get; set; }
        public int CompletedRequired { get; set; }
        public int Missing { get; set; }
        public int AwaitingReview { get; set; }
        public int ReplacementRequired { get; set; }
        public int Postponed { get; set; }
        public int PostponedDue { get; set; }
        public bool IsComplete { get; set; }
        public int CompletionPercent { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public class CustomerDocumentChecklistDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public CustomerDocumentSummaryDto Summary { get; set; } = new();
        public List<CustomerDocumentRequirementDto> Requirements { get; set; } = [];
        /// <summary>The most recent audit entries only; page the full log via the history endpoint when <see cref="HasMoreHistory"/> is true.</summary>
        public List<CustomerDocumentAuditDto> History { get; set; } = [];
        public bool HasMoreHistory { get; set; }
    }

    public class CustomerDocumentRequirementDto
    {
        public int Id { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public bool CategoryIsActive { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public List<string> AllowedFileTypes { get; set; } = [];
        public long MaxFileSizeBytes { get; set; }
        public CustomerDocumentStatus Status { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? PostponedUntil { get; set; }
        public string? LastActionByName { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
        public CustomerDocumentVersionDto? LatestVersion { get; set; }
        public List<CustomerDocumentVersionDto> Versions { get; set; } = [];
    }

    public class CustomerDocumentVersionDto
    {
        public int Id { get; set; }
        public int VersionNumber { get; set; }
        public bool IsCurrent { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
        public CustomerDocumentVersionStatus ReviewStatus { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewReason { get; set; }
    }

    public class CustomerDocumentAuditDto
    {
        public long Id { get; set; }
        public int? RequirementId { get; set; }
        public int? VersionId { get; set; }
        public string? DocumentName { get; set; }
        public CustomerDocumentAction Action { get; set; }
        public CustomerDocumentStatus? PreviousStatus { get; set; }
        public CustomerDocumentStatus? NewStatus { get; set; }
        public string? Notes { get; set; }
        public string? PerformedByName { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public class CustomerDocumentUpload
    {
        public Stream Content { get; set; } = Stream.Null;
        public string FileName { get; set; } = string.Empty;
        public long Length { get; set; }
    }

    public class CustomerDocumentDownload
    {
        public Stream Content { get; set; } = Stream.Null;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }
}
