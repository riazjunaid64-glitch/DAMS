using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingRequestDtos
{
    public class BookingRequestListDto
    {
        public List<BookingRequestResponseDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class BookingRequestFilterDto
    {
        public BookingRequestStatus? Status { get; set; }

        public int? ProjectId { get; set; }

        public string? SearchTerm { get; set; }

        public DateTime? RequestedFrom { get; set; }

        public DateTime? RequestedTo { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string SortBy { get; set; } = "RequestedAt";

        public bool SortDescending { get; set; } = true;
    }
}
