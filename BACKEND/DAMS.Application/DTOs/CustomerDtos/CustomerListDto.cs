using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    /// <summary>One row on the Customers list (KAN-79).</summary>
    public class CustomerListItemDto
    {
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? CNIC { get; set; }

        public bool IsBlocked { get; set; }

        public int BookingsCount { get; set; }

        /// <summary>Required documents still Needed (Missing / Requested / … — see CustomerDocumentsNeeded).</summary>
        public int DocumentsNeeded { get; set; }
    }

    public class CustomerListDto
    {
        public List<CustomerListItemDto> Items { get; set; } = new();

        /// <summary>Rows matching the search and the card filter (documents needed only).</summary>
        public int TotalCount { get; set; }

        /// <summary>Customers matching the search, ignoring the card filter.</summary>
        public int TotalCustomers { get; set; }

        /// <summary>Of those matching the search, how many still have at least one document needed.</summary>
        public int DocumentsNeededCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    public class CustomerFilterDto
    {
        public CustomerSource? Source { get; set; }

        public CustomerStatus? Status { get; set; }

        public string? SearchTerm { get; set; }

        /// <summary>When true, only customers with at least one required document still Needed.</summary>
        public bool DocumentsNeededOnly { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
