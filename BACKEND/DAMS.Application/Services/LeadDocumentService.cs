using DAMS.Application.Common;
using DAMS.Application.DTOs.LeadDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services
{
    public class LeadDocumentService : ILeadDocumentService
    {
        /// <summary>Lead paperwork is quotations and plans, not media libraries.</summary>
        public const long MaxFileSize = 15 * 1024 * 1024;

        public const long MaxRequestSize = MaxFileSize + (512 * 1024);

        private readonly AppDbContext _context;
        private readonly ILeadDocumentStorage _storage;
        private readonly ILogger<LeadDocumentService> _logger;

        public LeadDocumentService(AppDbContext context, ILeadDocumentStorage storage, ILogger<LeadDocumentService> logger)
        {
            _context = context;
            _storage = storage;
            _logger = logger;
        }

        public async Task<LeadDocumentDto> UploadAsync(
            int leadId,
            LeadDocumentUpload upload,
            LeadDocumentCategory category,
            string? description,
            int? communicationId,
            LeadUserContext ctx,
            CancellationToken cancellationToken = default)
        {
            var lead = await LeadGate.LoadActiveAsync(_context, leadId, ctx, cancellationToken);

            if (communicationId.HasValue)
            {
                var belongs = await _context.LeadCommunications
                    .AnyAsync(c => c.Id == communicationId.Value && c.LeadId == leadId, cancellationToken);

                if (!belongs)
                    throw new InvalidOperationException("That communication does not belong to this lead.");
            }

            var validated = UploadedFileValidator.Validate(upload.Content, upload.FileName, upload.Length, MaxFileSize);

            // Bytes first: a failed upload must not leave a metadata row pointing nowhere.
            var storedFileName = await _storage.SaveAsync(upload.Content, validated.Extension, cancellationToken);
            var rowCommitted = false;

            try
            {
                var document = new LeadDocument
                {
                    Lead = lead,
                    LeadId = lead.Id,
                    CommunicationId = communicationId,
                    Category = category,
                    StoredFileName = storedFileName,
                    OriginalFileName = validated.OriginalFileName,
                    ContentType = validated.ContentType,
                    FileSize = validated.FileSize,
                    Description = LeadContactNormalizer.LimitOrNull(LeadContactNormalizer.Clean(description), 500),
                    UploadedByUserId = ctx.UserId,
                    UploadedByName = ctx.DisplayName,
                    UploadedAt = DateTime.UtcNow
                };

                _context.LeadDocuments.Add(document);

                var activity = LeadTimeline.Record(_context, lead, LeadActivityType.DocumentUploaded,
                    $"{category} document uploaded.", ctx,
                    a =>
                    {
                        a.Notes = document.OriginalFileName;
                        a.CommunicationId = communicationId;
                    });

                lead.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
                rowCommitted = true;

                // The timeline link needs the generated id. If this second save fails the
                // document itself is already safely stored — only the cross-reference is
                // missing, so the file must stay.
                activity.DocumentId = document.Id;
                await _context.SaveChangesAsync(cancellationToken);

                return await LoadAsync(document.Id, cancellationToken);
            }
            catch when (!rowCommitted)
            {
                // Nothing was committed, so the orphaned bytes must go.
                await SafeDeleteAsync(storedFileName, cancellationToken);
                throw;
            }
        }

        public async Task<List<LeadDocumentDto>> GetForLeadAsync(
            int leadId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            await LeadGate.EnsureVisibleAsync(_context, leadId, ctx, cancellationToken);

            return await _context.LeadDocuments
                .AsNoTracking()
                .Where(d => d.LeadId == leadId)
                .OrderByDescending(d => d.UploadedAt)
                .Select(LeadMapping.ToDocumentDto)
                .ToListAsync(cancellationToken);
        }

        public async Task<LeadDocumentDownload> DownloadAsync(
            int documentId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var document = await LoadForWriteAsync(documentId, ctx, cancellationToken);

            var content = await _storage.OpenReadAsync(document.StoredFileName, cancellationToken);
            if (content == null)
            {
                _logger.LogWarning("Lead document {DocumentId} has no stored file {StoredFileName}.",
                    document.Id, document.StoredFileName);
                throw new FileNotFoundException("The stored file is missing. Ask the uploader to add it again.");
            }

            return new LeadDocumentDownload
            {
                Content = content,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType
            };
        }

        public async Task DeleteAsync(int documentId, LeadUserContext ctx, CancellationToken cancellationToken = default)
        {
            var document = await LoadForWriteAsync(documentId, ctx, cancellationToken);

            // Removing paperwork is a supervisory act — and the uploader's own mistake.
            if (!ctx.IsAdmin && !ctx.IsManager && document.UploadedByUserId != ctx.UserId)
                throw new LeadAuthorizationException("You can only remove documents you uploaded.");

            var lead = await LeadGate.LoadActiveAsync(_context, document.LeadId, ctx, cancellationToken);
            var storedFileName = document.StoredFileName;

            _context.LeadDocuments.Remove(document);

            LeadTimeline.Record(_context, lead, LeadActivityType.DocumentRemoved,
                $"{document.Category} document removed.", ctx, a => a.Notes = document.OriginalFileName);

            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await SafeDeleteAsync(storedFileName, cancellationToken);
        }

        private async Task SafeDeleteAsync(string storedFileName, CancellationToken cancellationToken)
        {
            try
            {
                await _storage.DeleteAsync(storedFileName, cancellationToken);
            }
            catch (Exception ex)
            {
                // The database is already consistent; a stranded file is a cleanup task, not
                // a reason to fail the caller's request.
                _logger.LogWarning(ex, "Could not delete lead document file {StoredFileName}.", storedFileName);
            }
        }

        private async Task<LeadDocument> LoadForWriteAsync(int documentId, LeadUserContext ctx, CancellationToken cancellationToken)
        {
            LeadAccess.EnsureStaff(ctx);

            var document = await _context.LeadDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
                ?? throw new InvalidOperationException("Document not found.");

            // Documents inherit the lead's access rules; there is no direct-link bypass.
            await LeadGate.EnsureVisibleAsync(_context, document.LeadId, ctx, cancellationToken);

            return document;
        }

        private async Task<LeadDocumentDto> LoadAsync(int id, CancellationToken cancellationToken) =>
            await _context.LeadDocuments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(LeadMapping.ToDocumentDto)
                .FirstAsync(cancellationToken);
    }
}
