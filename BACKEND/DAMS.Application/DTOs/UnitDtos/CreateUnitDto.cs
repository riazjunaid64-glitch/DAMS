 namespace DAMS.Application.DTOs.UnitDtos
{
    public class CreateUnitDto
    {
    public int ProjectId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public int FloorNumber { get; set; }
    public decimal Size { get; set; }
    public decimal Price { get; set; }
    }


}