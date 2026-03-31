namespace DAMS.Application.DTOs.UnitDtos
{
    public class UpdateUnitDto
    {
      public string UnitNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal Size { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    }
}