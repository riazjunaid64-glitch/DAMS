using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DAMS.Application.Services
{
    /// <summary>
    /// The half of attaching a file that is the same wherever it happens: validate it, write the
    /// bytes, and make sure a failure on either side of the database save does not leave an orphan
    /// — a file on disk no row points at, or a row pointing at a file that was never written.
    /// <para>
    /// Extracted because loans and partner capital now carry evidence too, and the ordering below
    /// is the part that is easy to get wrong. The bytes are written BEFORE the row is saved, so a
    /// failed save has to delete what it wrote; the old file is deleted only AFTER the save
    /// succeeds, so a failed save leaves the record still pointing at a file that is still there.
    /// </para>
    /// </summary>
    public interface IFinanceAttachmentWriter
    {
        /// <summary>Validates and stores the bytes. Returns the key to put on the row.</summary>
        Task<StoredFinanceAttachment> StoreAsync(FinanceAttachmentUpload upload, CancellationToken cancellationToken = default);

        /// <summary>Deletes a file whose row never made it to the database. Never throws.</summary>
        Task DiscardAsync(string? storedFileName);

        /// <summary>Deletes a file no row points at any more. Never throws.</summary>
        Task ForgetAsync(string? storedFileName);

        /// <summary>Opens a stored file, or null when it is missing from storage.</summary>
        Task<Stream?> OpenAsync(string storedFileName, CancellationToken cancellationToken = default);
    }

    public sealed record StoredFinanceAttachment(string StoredFileName, ValidatedFinanceAttachment Metadata)
    {
        /// <summary>Writes this file's metadata onto a new or existing attachment row.</summary>
        public void ApplyTo(FinanceAttachment attachment)
        {
            attachment.StoredFileName = StoredFileName;
            attachment.OriginalFileName = Metadata.OriginalFileName;
            attachment.ContentType = Metadata.ContentType;
            attachment.FileSize = Metadata.FileSize;
            attachment.UploadedAt = DateTime.UtcNow;
        }
    }

    public sealed class FinanceAttachmentWriter : IFinanceAttachmentWriter
    {
        private readonly IFinanceAttachmentStorage _storage;
        private readonly ILogger<FinanceAttachmentWriter> _logger;

        public FinanceAttachmentWriter(IFinanceAttachmentStorage storage, ILogger<FinanceAttachmentWriter> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<StoredFinanceAttachment> StoreAsync(
            FinanceAttachmentUpload upload, CancellationToken cancellationToken = default)
        {
            var metadata = FinanceAttachmentFileValidator.Validate(upload);
            var storedFileName = await _storage.SaveAsync(upload.Content, metadata.Extension, cancellationToken);
            return new StoredFinanceAttachment(storedFileName, metadata);
        }

        public Task DiscardAsync(string? storedFileName) =>
            DeleteQuietlyAsync(storedFileName, "orphaned after a failed save");

        public Task ForgetAsync(string? storedFileName) =>
            DeleteQuietlyAsync(storedFileName, "no longer referenced");

        public Task<Stream?> OpenAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            _storage.OpenReadAsync(storedFileName, cancellationToken);

        // Cleanup failures are logged, never thrown: the money movement the caller was making has
        // already succeeded or already failed on its own terms, and a leftover file is a
        // housekeeping problem, not a reason to report the wrong outcome to the operator.
        private async Task DeleteQuietlyAsync(string? storedFileName, string why)
        {
            if (storedFileName == null)
                return;
            try
            {
                await _storage.DeleteAsync(storedFileName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not delete finance attachment {StoredFileName} ({Reason})", storedFileName, why);
            }
        }
    }
}
