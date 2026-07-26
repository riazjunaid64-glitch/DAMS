using System.ComponentModel.DataAnnotations;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.EmployeeDtos
{
    public class StaffDirectoryDto
    {
        public int EmployeeId { get; set; }
        public int? UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Role { get; set; }
        public int? TeamId { get; set; }
        public string? TeamName { get; set; }
        public EmployeeStatus Status { get; set; }
        public bool CanOwnLeads { get; set; }
    }

    public class StaffAccountDto : StaffDirectoryDto
    {
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }
        public bool IsTeamManager { get; set; }
    }

    public class CreateStaffAccountDto
    {
        /// <summary>Connect an existing login instead of creating a new one.</summary>
        public int? ExistingUserId { get; set; }

        /// <summary>Connect an existing employee instead of creating a new record.</summary>
        public int? ExistingEmployeeId { get; set; }

        [Required, StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(200)]
        public string Email { get; set; } = string.Empty;

        /// <summary>Required for a new login; ignored when ExistingUserId is supplied.</summary>
        [StringLength(200, MinimumLength = 8)]
        public string? TemporaryPassword { get; set; }

        /// <summary>Admin, Manager, or Employee.</summary>
        [Required, StringLength(30)]
        public string Role { get; set; } = string.Empty;

        public int? TeamId { get; set; }

        [Required, StringLength(100)]
        public string JobTitle { get; set; } = "Sales Executive";

        [Required, StringLength(100)]
        public string Department { get; set; } = "Sales";

        [Required, StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        public DateTime? JoinDate { get; set; }
    }

    public class UpdateStaffAccountDto
    {
        [Required, StringLength(30)]
        public string Role { get; set; } = string.Empty;

        /// <summary>Send null to leave unchanged, or -1 to remove team membership.</summary>
        public int? TeamId { get; set; }

        public EmployeeStatus? Status { get; set; }

        [StringLength(200, MinimumLength = 8)]
        public string? NewTemporaryPassword { get; set; }
    }

    public class LinkableUserDto
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class CustomerLookupDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? CNIC { get; set; }
    }
}
