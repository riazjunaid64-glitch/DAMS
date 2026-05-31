using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.CustomerDtos
{
    public class CustomerListDto
    {
        public List<CustomerResponseDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    public class CustomerFilterDto
    {
        public CustomerSource? Source { get; set; }

        public CustomerStatus? Status { get; set; }

        public string? SearchTerm { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
