using System.ComponentModel.DataAnnotations;
using DAMS.Application.Common;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class CreateCustomerDto
    {
        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        /// <summary>S/O, W/O, D/O — father's / husband's name on receipts.</summary>
        [StringLength(200)]
        public string? FatherName { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 7)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(50)]
        public string? CNIC { get; set; }

        /// <summary>Optional; a blank value means "no email" (see <see cref="OptionalInput"/>).</summary>
        [EmailAddress]
        [StringLength(200)]
        public string? Email { get => _email; set => _email = OptionalInput.BlankAsNull(value); }
        private string? _email;

        [StringLength(50)]
        public string? Whatsapp { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [StringLength(100)]
        public string? Nationality { get; set; }

        [StringLength(150)]
        public string? Occupation { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(500)]
        public string? SourceNotes { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
