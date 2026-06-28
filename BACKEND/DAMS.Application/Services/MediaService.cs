using DAMS.Application.DTOs.MediaDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    public class MediaService : IMediaService
    {
        private readonly AppDbContext _context;
        private readonly IFileStorageService _fileStorageService;

        // Configuration for file validation
        private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
        private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };
        private static readonly string[] AllowedVideoTypes = { "video/mp4", "video/webm", "video/quicktime" };
        private static readonly string[] AllowedDocumentTypes = { "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" };

        public MediaService(AppDbContext context, IFileStorageService fileStorageService)
        {
            _context = context;
            _fileStorageService = fileStorageService;
        }

        private void ValidateFile(Stream fileStream, string contentType, string fileName)
        {
            // Use the new FileValidationService
            FileValidationService.ValidateFile(fileStream, contentType, fileName);
        }

        public async Task<ProjectMediaResponseDto> UploadProjectMediaAsync(int projectId, Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto = null)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            // Validate file before uploading
            ValidateFile(fileStream, contentType, fileName);

            var sanitizedFileName = FileValidationService.SanitizeFileName(fileName);
            var fileSize = fileStream.Length; // capture before SaveFileAsync consumes/seeks the stream
            var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, sanitizedFileName, $"projects/{projectId}");

            // Get image dimensions if applicable
            var (width, height) = FileValidationService.GetImageDimensions(fileStream, contentType);

            var media = new ProjectMedia
            {
                ProjectId = projectId,
                MediaUrl = mediaUrl,
                MediaType = ResolveMediaType(contentType, fileName),
                UploadedAt = DateTime.UtcNow,
                Category = uploadDto?.Category ?? Domain.Enums.MediaCategory.Gallery,
                IsCover = uploadDto?.IsCover ?? false,
                AltText = uploadDto?.AltText,
                Description = uploadDto?.Description,
                FileSize = fileSize,
                Width = width,
                Height = height,
                OriginalFileName = sanitizedFileName,
                MimeType = contentType
            };

            // If this is set as cover, remove cover flag from other media
            if (media.IsCover)
            {
                await RemoveCoverFromProjectMediaAsync(projectId);
            }

            _context.ProjectMedias.Add(media);
            await _context.SaveChangesAsync();

            return MapToProjectMediaResponse(media);
        }

        public async Task<List<ProjectMediaResponseDto>> UploadProjectMediaBulkAsync(int projectId, List<(Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto)> files)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            var batchRequestsCover = files.Exists(f => f.uploadDto?.IsCover == true);
            if (batchRequestsCover)
            {
                await RemoveCoverFromProjectMediaAsync(projectId);
            }

            var results = new List<ProjectMediaResponseDto>();
            var coverSlotUsed = false;

            foreach (var (fileStream, fileName, contentType, uploadDto) in files)
            {
                try
                {
                    ValidateFile(fileStream, contentType, fileName);

                    var sanitizedFileName = FileValidationService.SanitizeFileName(fileName);
                    var fileSize = fileStream.Length; // capture before SaveFileAsync consumes/seeks the stream
                    var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, sanitizedFileName, $"projects/{projectId}");

                    var (width, height) = FileValidationService.GetImageDimensions(fileStream, contentType);

                    var wantsCover = uploadDto?.IsCover ?? false;
                    var isCover = wantsCover && !coverSlotUsed;
                    if (isCover)
                    {
                        coverSlotUsed = true;
                    }

                    var media = new ProjectMedia
                    {
                        ProjectId = projectId,
                        MediaUrl = mediaUrl,
                        MediaType = ResolveMediaType(contentType, fileName),
                        UploadedAt = DateTime.UtcNow,
                        Category = uploadDto?.Category ?? Domain.Enums.MediaCategory.Gallery,
                        IsCover = isCover,
                        AltText = uploadDto?.AltText,
                        Description = uploadDto?.Description,
                        FileSize = fileSize,
                        Width = width,
                        Height = height,
                        OriginalFileName = sanitizedFileName,
                        MimeType = contentType
                    };

                    results.Add(MapToProjectMediaResponse(media));
                    _context.ProjectMedias.Add(media);
                }
                catch (Exception ex)
                {
                    // Log error but continue with other files
                    Console.WriteLine($"Error uploading file {fileName}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();
            return results;
        }

        public async Task<List<ProjectMediaResponseDto>> GetProjectMediaAsync(int projectId)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            var media = await _context.ProjectMedias
                .AsNoTracking()
                .Where(pm => pm.ProjectId == projectId)
                .OrderBy(pm => pm.IsCover ? 0 : 1)
                .ThenBy(pm => pm.DisplayOrder)
                .ThenByDescending(pm => pm.UploadedAt)
                .ToListAsync();

            return media.Select(MapToProjectMediaResponse).ToList();
        }

        public async Task<ProjectMediaResponseDto?> UpdateProjectMediaAsync(int projectId, int mediaId, UpdateMediaDto updateDto)
        {
            var media = await _context.ProjectMedias
                .FirstOrDefaultAsync(pm => pm.Id == mediaId && pm.ProjectId == projectId);
            if (media == null)
            {
                throw new Exception("Project media not found.");
            }

            if (updateDto.Category.HasValue)
                media.Category = updateDto.Category.Value;

            if (updateDto.IsCover.HasValue)
            {
                if (updateDto.IsCover.Value)
                {
                    await RemoveCoverFromProjectMediaAsync(media.ProjectId);
                }
                media.IsCover = updateDto.IsCover.Value;
            }

            if (updateDto.DisplayOrder.HasValue)
                media.DisplayOrder = updateDto.DisplayOrder.Value;

            if (updateDto.AltText != null)
                media.AltText = updateDto.AltText;

            if (updateDto.Description != null)
                media.Description = updateDto.Description;

            media.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToProjectMediaResponse(media);
        }

        public async Task<bool> DeleteProjectMediaAsync(int projectId, int mediaId)
        {
            var media = await _context.ProjectMedias
                .FirstOrDefaultAsync(pm => pm.Id == mediaId && pm.ProjectId == projectId);
            if (media == null)
            {
                throw new Exception("Project media not found.");
            }

            _context.ProjectMedias.Remove(media);
            await _context.SaveChangesAsync();

            await _fileStorageService.DeleteFileAsync(media.MediaUrl);
            return true;
        }

        public async Task<bool> ReorderProjectMediaAsync(int projectId, List<int> mediaIds)
        {
            var mediaList = await _context.ProjectMedias
                .Where(pm => pm.ProjectId == projectId && mediaIds.Contains(pm.Id))
                .ToListAsync();

            for (int i = 0; i < mediaIds.Count; i++)
            {
                var media = mediaList.FirstOrDefault(m => m.Id == mediaIds[i]);
                if (media != null)
                {
                    media.DisplayOrder = i;
                    media.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetProjectCoverMediaAsync(int projectId, int mediaId)
        {
            await RemoveCoverFromProjectMediaAsync(projectId);

            var media = await _context.ProjectMedias
                .FirstOrDefaultAsync(pm => pm.Id == mediaId && pm.ProjectId == projectId);
            
            if (media == null)
            {
                throw new Exception("Project media not found.");
            }

            media.IsCover = true;
            media.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<UnitMediaResponseDto> UploadUnitMediaAsync(int unitId, Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto = null)
        {
            var unitExists = await _context.Units.AnyAsync(u => u.Id == unitId);
            if (!unitExists)
            {
                throw new Exception("Unit not found.");
            }

            // Validate file before uploading
            ValidateFile(fileStream, contentType, fileName);

            var sanitizedFileName = FileValidationService.SanitizeFileName(fileName);
            var fileSize = fileStream.Length; // capture before SaveFileAsync consumes/seeks the stream
            var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, sanitizedFileName, $"units/{unitId}");

            var (width, height) = FileValidationService.GetImageDimensions(fileStream, contentType);

            var media = new UnitMedia
            {
                UnitId = unitId,
                MediaUrl = mediaUrl,
                MediaType = ResolveMediaType(contentType, fileName),
                UploadedAt = DateTime.UtcNow,
                Category = uploadDto?.Category ?? Domain.Enums.MediaCategory.Gallery,
                IsCover = uploadDto?.IsCover ?? false,
                AltText = uploadDto?.AltText,
                Description = uploadDto?.Description,
                FileSize = fileSize,
                Width = width,
                Height = height,
                OriginalFileName = sanitizedFileName,
                MimeType = contentType
            };

            // If this is set as cover, remove cover flag from other media
            if (media.IsCover)
            {
                await RemoveCoverFromUnitMediaAsync(unitId);
            }

            _context.UnitMedias.Add(media);
            await _context.SaveChangesAsync();

            return MapToUnitMediaResponse(media);
        }

        public async Task<List<UnitMediaResponseDto>> UploadUnitMediaBulkAsync(int unitId, List<(Stream fileStream, string fileName, string contentType, UploadMediaDto? uploadDto)> files)
        {
            var unitExists = await _context.Units.AnyAsync(u => u.Id == unitId);
            if (!unitExists)
            {
                throw new Exception("Unit not found.");
            }

            var batchRequestsCover = files.Exists(f => f.uploadDto?.IsCover == true);
            if (batchRequestsCover)
            {
                await RemoveCoverFromUnitMediaAsync(unitId);
            }

            var results = new List<UnitMediaResponseDto>();
            var coverSlotUsed = false;

            foreach (var (fileStream, fileName, contentType, uploadDto) in files)
            {
                try
                {
                    ValidateFile(fileStream, contentType, fileName);

                    var sanitizedFileName = FileValidationService.SanitizeFileName(fileName);
                    var fileSize = fileStream.Length; // capture before SaveFileAsync consumes/seeks the stream
                    var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, sanitizedFileName, $"units/{unitId}");

                    var (width, height) = FileValidationService.GetImageDimensions(fileStream, contentType);

                    var wantsCover = uploadDto?.IsCover ?? false;
                    var isCover = wantsCover && !coverSlotUsed;
                    if (isCover)
                    {
                        coverSlotUsed = true;
                    }

                    var media = new UnitMedia
                    {
                        UnitId = unitId,
                        MediaUrl = mediaUrl,
                        MediaType = ResolveMediaType(contentType, fileName),
                        UploadedAt = DateTime.UtcNow,
                        Category = uploadDto?.Category ?? Domain.Enums.MediaCategory.Gallery,
                        IsCover = isCover,
                        AltText = uploadDto?.AltText,
                        Description = uploadDto?.Description,
                        FileSize = fileSize,
                        Width = width,
                        Height = height,
                        OriginalFileName = sanitizedFileName,
                        MimeType = contentType
                    };

                    results.Add(MapToUnitMediaResponse(media));
                    _context.UnitMedias.Add(media);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading file {fileName}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();
            return results;
        }

        public async Task<List<UnitMediaResponseDto>> GetUnitMediaAsync(int unitId)
        {
            var unitExists = await _context.Units.AnyAsync(u => u.Id == unitId);
            if (!unitExists)
            {
                throw new Exception("Unit not found.");
            }

            var media = await _context.UnitMedias
                .AsNoTracking()
                .Where(um => um.UnitId == unitId)
                .OrderBy(um => um.IsCover ? 0 : 1)
                .ThenBy(um => um.DisplayOrder)
                .ThenByDescending(um => um.UploadedAt)
                .ToListAsync();

            return media.Select(MapToUnitMediaResponse).ToList();
        }

        public async Task<UnitMediaResponseDto?> UpdateUnitMediaAsync(int unitId, int mediaId, UpdateMediaDto updateDto)
        {
            var media = await _context.UnitMedias
                .FirstOrDefaultAsync(um => um.Id == mediaId && um.UnitId == unitId);
            if (media == null)
            {
                throw new Exception("Unit media not found.");
            }

            if (updateDto.Category.HasValue)
                media.Category = updateDto.Category.Value;

            if (updateDto.IsCover.HasValue)
            {
                if (updateDto.IsCover.Value)
                {
                    await RemoveCoverFromUnitMediaAsync(media.UnitId);
                }
                media.IsCover = updateDto.IsCover.Value;
            }

            if (updateDto.DisplayOrder.HasValue)
                media.DisplayOrder = updateDto.DisplayOrder.Value;

            if (updateDto.AltText != null)
                media.AltText = updateDto.AltText;

            if (updateDto.Description != null)
                media.Description = updateDto.Description;

            media.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return MapToUnitMediaResponse(media);
        }

        public async Task<bool> DeleteUnitMediaAsync(int unitId, int mediaId)
        {
            var media = await _context.UnitMedias
                .FirstOrDefaultAsync(um => um.Id == mediaId && um.UnitId == unitId);
            if (media == null)
            {
                throw new Exception("Unit media not found.");
            }

            _context.UnitMedias.Remove(media);
            await _context.SaveChangesAsync();

            await _fileStorageService.DeleteFileAsync(media.MediaUrl);
            return true;
        }

        public async Task<bool> ReorderUnitMediaAsync(int unitId, List<int> mediaIds)
        {
            var mediaList = await _context.UnitMedias
                .Where(um => um.UnitId == unitId && mediaIds.Contains(um.Id))
                .ToListAsync();

            for (int i = 0; i < mediaIds.Count; i++)
            {
                var media = mediaList.FirstOrDefault(m => m.Id == mediaIds[i]);
                if (media != null)
                {
                    media.DisplayOrder = i;
                    media.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetUnitCoverMediaAsync(int unitId, int mediaId)
        {
            await RemoveCoverFromUnitMediaAsync(unitId);

            var media = await _context.UnitMedias
                .FirstOrDefaultAsync(um => um.Id == mediaId && um.UnitId == unitId);
            
            if (media == null)
            {
                throw new Exception("Unit media not found.");
            }

            media.IsCover = true;
            media.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        private async Task RemoveCoverFromProjectMediaAsync(int projectId)
        {
            var coverMedia = await _context.ProjectMedias
                .Where(pm => pm.ProjectId == projectId && pm.IsCover)
                .ToListAsync();

            foreach (var media in coverMedia)
            {
                media.IsCover = false;
                media.UpdatedAt = DateTime.UtcNow;
            }
        }

        private async Task RemoveCoverFromUnitMediaAsync(int unitId)
        {
            var coverMedia = await _context.UnitMedias
                .Where(um => um.UnitId == unitId && um.IsCover)
                .ToListAsync();

            foreach (var media in coverMedia)
            {
                media.IsCover = false;
                media.UpdatedAt = DateTime.UtcNow;
            }
        }

        private static string ResolveMediaType(string contentType, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return "image";
                }

                if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    return "video";
                }
            }

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp")
            {
                return "image";
            }

            if (ext is ".mp4" or ".webm" or ".mov")
            {
                return "video";
            }

            return "file";
        }

        private static ProjectMediaResponseDto MapToProjectMediaResponse(ProjectMedia media)
        {
            return new ProjectMediaResponseDto
            {
                Id = media.Id,
                ProjectId = media.ProjectId,
                MediaUrl = media.MediaUrl,
                MediaType = media.MediaType,
                UploadedAt = media.UploadedAt,
                Category = media.Category,
                IsCover = media.IsCover,
                DisplayOrder = media.DisplayOrder,
                AltText = media.AltText,
                Description = media.Description,
                FileSize = media.FileSize,
                Width = media.Width,
                Height = media.Height,
                OriginalFileName = media.OriginalFileName,
                MimeType = media.MimeType,
                UpdatedAt = media.UpdatedAt
            };
        }

        private static UnitMediaResponseDto MapToUnitMediaResponse(UnitMedia media)
        {
            return new UnitMediaResponseDto
            {
                Id = media.Id,
                UnitId = media.UnitId,
                MediaUrl = media.MediaUrl,
                MediaType = media.MediaType,
                UploadedAt = media.UploadedAt,
                Category = media.Category,
                IsCover = media.IsCover,
                DisplayOrder = media.DisplayOrder,
                AltText = media.AltText,
                Description = media.Description,
                FileSize = media.FileSize,
                Width = media.Width,
                Height = media.Height,
                OriginalFileName = media.OriginalFileName,
                MimeType = media.MimeType,
                UpdatedAt = media.UpdatedAt
            };
        }
    }
}
