using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using DAMS.Application.Common;
using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class UpdateCustomerDto : IValidatableObject
    {
        private readonly HashSet<string> _provided = [];
        private string _fullName = string.Empty;
        private string? _fatherName;
        private string _phone = string.Empty;
        private string? _cnic;
        private string? _email;
        private string? _address;
        private CustomerStatus _status;
        private string? _sourceNotes;
        private string? _notes;

        [StringLength(200)]
        public string FullName { get => _fullName; set { _provided.Add(nameof(FullName)); _fullName = value; } }

        [StringLength(200)]
        public string? FatherName { get => _fatherName; set { _provided.Add(nameof(FatherName)); _fatherName = value; } }

        [StringLength(50)]
        public string Phone { get => _phone; set { _provided.Add(nameof(Phone)); _phone = value; } }

        [StringLength(50)]
        public string? CNIC { get => _cnic; set { _provided.Add(nameof(CNIC)); _cnic = value; } }

        /// <summary>Optional; a blank value means "no email" (see <see cref="OptionalInput"/>).</summary>
        [EmailAddress]
        [StringLength(200)]
        public string? Email { get => _email; set { _provided.Add(nameof(Email)); _email = OptionalInput.BlankAsNull(value); } }

        [StringLength(500)]
        public string? Address { get => _address; set { _provided.Add(nameof(Address)); _address = value; } }

        public CustomerStatus Status { get => _status; set { _provided.Add(nameof(Status)); _status = value; } }

        [StringLength(500)]
        public string? SourceNotes { get => _sourceNotes; set { _provided.Add(nameof(SourceNotes)); _sourceNotes = value; } }

        [StringLength(1000)]
        public string? Notes { get => _notes; set { _provided.Add(nameof(Notes)); _notes = value; } }

        public bool WasProvided(string propertyName) => _provided.Contains(propertyName);

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (WasProvided(nameof(FullName)) && (string.IsNullOrWhiteSpace(FullName) || FullName.Trim().Length < 2))
                yield return new ValidationResult("The FullName field must contain at least 2 characters.", [nameof(FullName)]);
            if (WasProvided(nameof(Phone)) && (string.IsNullOrWhiteSpace(Phone) || Phone.Trim().Length < 7))
                yield return new ValidationResult("The Phone field must contain at least 7 characters.", [nameof(Phone)]);
        }
    }
}
