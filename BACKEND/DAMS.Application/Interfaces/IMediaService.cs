using DAMS.Application.DTOs.MediaDtos;

namespace DAMS.Application.Interfaces
{
    public interface IMediaService
    {
        // Project Media
        Task<ProjectMediaResponseDto> UploadProjectMediaAsync(int projectId, Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto = null);
        Task<List<ProjectMediaResponseDto>> UploadProjectMediaBulkAsync(int projectId, List<(Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto)> files);
        Task<List<ProjectMediaResponseDto>> GetProjectMediaAsync(int projectId);
        Task<ProjectMediaResponseDto?> UpdateProjectMediaAsync(int projectId, int mediaId, UpdateMediaDto updateDto);
        Task<bool> DeleteProjectMediaAsync(int projectId, int mediaId);
        Task<bool> ReorderProjectMediaAsync(int projectId, List<int> mediaIds);
        Task<bool> SetProjectCoverMediaAsync(int projectId, int mediaId);

        // Unit Media
        Task<UnitMediaResponseDto> UploadUnitMediaAsync(int unitId, Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto = null);
        Task<List<UnitMediaResponseDto>> UploadUnitMediaBulkAsync(int unitId, List<(Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto)> files);
        Task<List<UnitMediaResponseDto>> GetUnitMediaAsync(int unitId);
        Task<List<UnitMediaResponseDto>> GetUnitMediaByProjectAsync(int projectId);
        Task<UnitMediaResponseDto?> UpdateUnitMediaAsync(int unitId, int mediaId, UpdateMediaDto updateDto);
        Task<bool> DeleteUnitMediaAsync(int unitId, int mediaId);
        Task<bool> ReorderUnitMediaAsync(int unitId, List<int> mediaIds);
        Task<bool> SetUnitCoverMediaAsync(int unitId, int mediaId);
    }
}
