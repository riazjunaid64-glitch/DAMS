using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.LeadDtos
{
    public class LeadSourceDto
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public bool IsSystem { get; set; }

        public int DisplayOrder { get; set; }

        public CustomerSource CustomerSource { get; set; }
    }

    public class CreateLeadSourceDto
    {
        [Required]
        [StringLength(50, MinimumLength = 2)]
        [RegularExpression("^[a-z0-9_]+$", ErrorMessage = "Code may contain lowercase letters, digits and underscores only.")]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        /// <summary>How this source is recorded on a converted Customer/Booking.</summary>
        public CustomerSource CustomerSource { get; set; } = CustomerSource.Other;
    }

    public class UpdateLeadSourceDto
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }

        public CustomerSource CustomerSource { get; set; } = CustomerSource.Other;
    }

    public class LeadClosureReasonDto
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public LeadClosureReasonKind Kind { get; set; }

        public bool IsActive { get; set; }

        public bool IsSystem { get; set; }

        public int DisplayOrder { get; set; }
    }

    public class CreateLeadClosureReasonDto
    {
        [Required]
        [StringLength(50, MinimumLength = 2)]
        [RegularExpression("^[a-z0-9_]+$", ErrorMessage = "Code may contain lowercase letters, digits and underscores only.")]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(150, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public LeadClosureReasonKind Kind { get; set; } = LeadClosureReasonKind.Both;

        public int DisplayOrder { get; set; }
    }

    public class UpdateLeadClosureReasonDto
    {
        [Required]
        [StringLength(150, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public LeadClosureReasonKind Kind { get; set; } = LeadClosureReasonKind.Both;

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }
    }

    public class TeamDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? ManagerEmployeeId { get; set; }

        public string? ManagerName { get; set; }

        public bool IsActive { get; set; }

        public int MemberCount { get; set; }
    }

    public class SaveTeamDto
    {
        [Required]
        [StringLength(150, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public int? ManagerEmployeeId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
