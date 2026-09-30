namespace DAMS.Application.DTOs.UnitDtos
{
    public class UnitResponseDto
    {
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public string FloorName { get; set; } = string.Empty;
    public decimal Size { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;

    // The unit's live (not cancelled) booking. Filled only for people who may open bookings, so a
    // booked unit can link to it; left null for everyone else.
    public int? LiveBookingId { get; set; }
    public string? LiveBookingReference { get; set; }
    }
}