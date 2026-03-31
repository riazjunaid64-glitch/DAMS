using  DAMS.Application.DTOs.UnitDtos;

namespace DAMS.Application.Interfaces
{
    public interface IUnitService
    {
    Task<UnitResponseDto> CreateUnitAsync(CreateUnitDto dto);
    Task<List<UnitResponseDto>> GetUnitsByProjectIdAsync(int projectId);
    Task<UnitResponseDto> UpdateUnitAsync(int id, UpdateUnitDto dto);
    Task<bool> DeleteUnitAsync(int id);
    }
}