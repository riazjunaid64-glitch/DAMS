using  DAMS.Application.DTOs.UnitDtos;

namespace DAMS.Application.Interfaces
{
    public interface IUnitService
    {
    Task<UnitResponseDto> CreateUnitAsync(CreateUnitDto dto);
    /// <param name="includeLiveBooking">True only for someone who may open bookings: the unit then names its live booking.</param>
    Task<UnitResponseDto?> GetUnitByIdAsync(int id, bool includeLiveBooking = false);
    Task<List<UnitResponseDto>> GetUnitsByProjectIdAsync(int projectId);
    Task<UnitResponseDto> UpdateUnitAsync(int id, UpdateUnitDto dto);
    Task<bool> DeleteUnitAsync(int id);
    }
}