using DAMS.Application.DTOs.MediaDtos;

namespace DAMS.Application.Interfaces
{
    public interface IMediaService
    {
        Task<ProjectMediaResponseDto> UploadProjectMediaAsync(int projectId, Stream fileStream, string fileName, string contentType);
        Task<List<ProjectMediaResponseDto>> GetProjectMediaAsync(int projectId);
        Task<bool> DeleteProjectMediaAsync(int mediaId);

        Task<UnitMediaResponseDto> UploadUnitMediaAsync(int unitId, Stream fileStream, string fileName, string contentType);
        Task<List<UnitMediaResponseDto>> GetUnitMediaAsync(int unitId);
        Task<List<UnitMediaResponseDto>> GetUnitMediaByProjectAsync(int projectId);
        Task<bool> DeleteUnitMediaAsync(int mediaId);
    }
}
