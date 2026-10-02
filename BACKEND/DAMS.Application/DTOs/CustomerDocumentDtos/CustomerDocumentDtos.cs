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
        public bool IsActive { get; set; }
        public bool AssignToNewCustomers { get; set; }
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

    public class NotNeededDocumentDto
    {
        [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
        [Required] public string ConcurrencyToken { get; set; } = string.Empty;
    }

    public class CustomerDocumentChecklistDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        /// <summary>Documents asked from this customer that still have no file (the same number as the page header).</summary>
        public int StillNeeded { get; set; }
        /// <summary>Documents that are uploaded or marked not needed.</summary>
        public int Done { get; set; }
        public List<CustomerDocumentRequirementDto> Requirements { get; set; } = [];
        /// <summary>Types the Add document popup offers: not asked from every customer and not already on this customer.</summary>
        public List<CustomerDocumentTypeOptionDto> AvailableTypes { get; set; } = [];
    }

    public class CustomerDocumentTypeOptionDto
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CustomerDocumentRequirementDto
    {
        public int Id { get; set; }
        public int? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public CustomerDocumentStatus Status { get; set; }
        public string? NotNeededReason { get; set; }
        public string? NotNeededByName { get; set; }
        public DateTime? NotNeededAt { get; set; }
        public string? LastActionByName { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
        public CustomerDocumentVersionDto? LatestVersion { get; set; }
        /// <summary>The most recent files only (newest first); older files exist when <see cref="HasMoreVersions"/> is true.</summary>
        public List<CustomerDocumentVersionDto> Versions { get; set; } = [];
        public bool HasMoreVersions { get; set; }
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
