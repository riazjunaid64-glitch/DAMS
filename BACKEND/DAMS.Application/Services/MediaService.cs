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

        public MediaService(AppDbContext context, IFileStorageService fileStorageService)
        {
            _context = context;
            _fileStorageService = fileStorageService;
        }

        public async Task<ProjectMediaResponseDto> UploadProjectMediaAsync(int projectId, Stream fileStream, string fileName, string contentType)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, fileName, $"projects/{projectId}");

            var media = new ProjectMedia
            {
                ProjectId = projectId,
                MediaUrl = mediaUrl,
                UploadedAt = DateTime.UtcNow
            };

            _context.ProjectMedias.Add(media);
            await _context.SaveChangesAsync();

            return new ProjectMediaResponseDto
            {
                Id = media.Id,
                ProjectId = media.ProjectId,
                MediaUrl = media.MediaUrl,
                MediaType = ResolveMediaType(contentType, fileName),
                UploadedAt = media.UploadedAt
            };
        }

        public async Task<List<ProjectMediaResponseDto>> GetProjectMediaAsync(int projectId)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            var media = await _context.ProjectMedias
                .Where(pm => pm.ProjectId == projectId)
                .OrderByDescending(pm => pm.UploadedAt)
                .ToListAsync();

            return media.Select(pm => new ProjectMediaResponseDto
            {
                Id = pm.Id,
                ProjectId = pm.ProjectId,
                MediaUrl = pm.MediaUrl,
                MediaType = ResolveMediaType(string.Empty, pm.MediaUrl),
                UploadedAt = pm.UploadedAt
            }).ToList();
        }

        public async Task<bool> DeleteProjectMediaAsync(int mediaId)
        {
            var media = await _context.ProjectMedias.FindAsync(mediaId);
            if (media == null)
            {
                throw new Exception("Project media not found.");
            }

            _context.ProjectMedias.Remove(media);
            await _context.SaveChangesAsync();

            await _fileStorageService.DeleteFileAsync(media.MediaUrl);
            return true;
        }

        public async Task<UnitMediaResponseDto> UploadUnitMediaAsync(int unitId, Stream fileStream, string fileName, string contentType)
        {
            var unitExists = await _context.Units.AnyAsync(u => u.Id == unitId);
            if (!unitExists)
            {
                throw new Exception("Unit not found.");
            }

            var mediaUrl = await _fileStorageService.SaveFileAsync(fileStream, fileName, $"units/{unitId}");

            var media = new UnitMedia
            {
                UnitId = unitId,
                MediaUrl = mediaUrl,
                MediaType = ResolveMediaType(contentType, fileName),
                UploadedAt = DateTime.UtcNow
            };

            _context.UnitMedias.Add(media);
            await _context.SaveChangesAsync();

            return new UnitMediaResponseDto
            {
                Id = media.Id,
                UnitId = media.UnitId,
                MediaUrl = media.MediaUrl,
                MediaType = media.MediaType,
                UploadedAt = media.UploadedAt
            };
        }

        public async Task<List<UnitMediaResponseDto>> GetUnitMediaAsync(int unitId)
        {
            var unitExists = await _context.Units.AnyAsync(u => u.Id == unitId);
            if (!unitExists)
            {
                throw new Exception("Unit not found.");
            }

            var media = await _context.UnitMedias
                .Where(um => um.UnitId == unitId)
                .OrderByDescending(um => um.UploadedAt)
                .ToListAsync();

            return media.Select(um => new UnitMediaResponseDto
            {
                Id = um.Id,
                UnitId = um.UnitId,
                MediaUrl = um.MediaUrl,
                MediaType = um.MediaType,
                UploadedAt = um.UploadedAt
            }).ToList();
        }

        public async Task<List<UnitMediaResponseDto>> GetUnitMediaByProjectAsync(int projectId)
        {
            var projectExists = await _context.Projects.AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                throw new Exception("Project not found.");
            }

            var media = await _context.UnitMedias
                .Where(um => um.Unit.ProjectId == projectId)
                .OrderByDescending(um => um.UploadedAt)
                .ToListAsync();

            return media.Select(um => new UnitMediaResponseDto
            {
                Id = um.Id,
                UnitId = um.UnitId,
                MediaUrl = um.MediaUrl,
                MediaType = um.MediaType,
                UploadedAt = um.UploadedAt
            }).ToList();
        }

        public async Task<bool> DeleteUnitMediaAsync(int mediaId)
        {
            var media = await _context.UnitMedias.FindAsync(mediaId);
            if (media == null)
            {
                throw new Exception("Unit media not found.");
            }

            _context.UnitMedias.Remove(media);
            await _context.SaveChangesAsync();

            await _fileStorageService.DeleteFileAsync(media.MediaUrl);
            return true;
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
    }
}
