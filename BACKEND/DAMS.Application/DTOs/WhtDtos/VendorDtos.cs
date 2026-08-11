using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.WhtDtos
{
    public class SaveVendorDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Ntn { get; set; }
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public FilerStatus FilerStatus { get; set; } = FilerStatus.Unknown;

        /// <summary>True when the admin has just re-checked this vendor against the FBR Active
        /// Taxpayer List; stamps <c>FilerStatusCheckedAt</c>.</summary>
        public bool MarkFilerStatusChecked { get; set; }

        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ConcurrencyToken { get; set; }
    }

    public class VendorOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public FilerStatus FilerStatus { get; set; }
        public string? Ntn { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class VendorDto : VendorOptionDto
    {
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Notes { get; set; }
        public DateTime? FilerStatusCheckedAt { get; set; }

        /// <summary>Gross spend and tax withheld in the current financial year — the numbers that
        /// decide whether the next payment crosses a threshold.</summary>
        public decimal YearToDateGross { get; set; }
        public decimal YearToDateWht { get; set; }
        public int ExpenseCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string ConcurrencyToken { get; set; } = string.Empty;
    }

    /// <summary>Per-section year-to-date position for one vendor, so the threshold notice can
    /// explain itself.</summary>
    public sealed class VendorYtdLineDto
    {
        public string Scope { get; set; } = string.Empty;
        public string FinancialYear { get; set; } = string.Empty;
        public decimal GrossPaid { get; set; }
        public decimal WhtWithheld { get; set; }
        public int ExpenseCount { get; set; }
    }
}
