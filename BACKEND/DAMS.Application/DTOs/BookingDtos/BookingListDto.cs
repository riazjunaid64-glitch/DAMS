using DAMS.Domain.Enums;

namespace DAMS.Application.DTOs.BookingDtos
{
    public class BookingListDto
    {
        public List<BookingResponseDto> Items { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    /// <summary>How many bookings are in each status for the current search and project.</summary>
    public class BookingStatusCountsDto
    {
        public int Total { get; set; }

        public int AwaitingBookingAmount { get; set; }

        public int PaymentPlanActive { get; set; }

        public int PossessionGiven { get; set; }

        public int SaleCompleted { get; set; }

        public int Cancelled { get; set; }
    }

    public class BookingFilterDto
    {
        public BookingStatus? Status { get; set; }

        public int? ProjectId { get; set; }

        public int? CustomerId { get; set; }

        public string? SearchTerm { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
