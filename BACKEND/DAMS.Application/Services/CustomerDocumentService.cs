using System.Data;
using System.Linq.Expressions;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace DAMS.Application.Services
{
    public class CustomerDocumentService : ICustomerDocumentService
    {
        public const long MaxFileSize = 10 * 1024 * 1024;
        public const long MaxRequestSize = MaxFileSize + (512 * 1024);
        private const int AssignmentBatchSize = 500;
        private const int HistoryPreviewSize = 100;
        // A requirement can accumulate many replacement versions over time; only the newest are
        // loaded/rendered so a long-lived requirement cannot force an unbounded read. HasMoreVersions
        // on the DTO signals that older versions exist beyond this preview.
        private const int VersionPreviewSize = 20;

        private static readonly Expression<Func<CustomerDocumentAuditEntry, CustomerDocumentAuditDto>> AuditProjection =
            a => new CustomerDocumentAuditDto
            {
                Id = a.Id,
                RequirementId = a.RequirementId,
                VersionId = a.VersionId,
                DocumentName = a.Requirement != null ? a.Requirement.Name : a.Category != null ? a.Category.Name : null,
                Action = a.Action,
                PreviousStatus = a.PreviousStatus,
                NewStatus = a.NewStatus,
                Notes = a.Notes,
                PerformedByName = a.PerformedByName,
                OccurredAt = a.OccurredAt
            };
        private const int NotNeededReasonMaxLength = 500;
        public const string NameClashMessage = "A document with this name already exists.";
        private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png"
        };

        private readonly AppDbContext _context;
        private readonly ICustomerDocumentStorage _storage;
        private readonly ILogger<CustomerDocumentService> _logger;

        public CustomerDocumentService(
            AppDbContext context,
            ICustomerDocumentStorage storage,
            ILogger<CustomerDocumentService> logger)
        {
            _context = context;
            _storage = storage;
            _logger = logger;
        }

        public async Task<DocumentSetupListDto> GetSetupAsync(CancellationToken cancellationToken = default)
        {
            var documents = await _context.CustomerDocumentCategories.AsNoTracking()
                .Where(c => !c.IsHidden && !c.IsOther)
                .OrderBy(c => c.Id)
                .Select(c => new DocumentSetupItemDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    AsksEveryCustomer = c.AsksEveryCustomer
                })
                .ToListAsync(cancellationToken);
            var nonBlocked = await _context.Customers.AsNoTracking()
                .CountAsync(c => c.Status != CustomerStatus.Blocked, cancellationToken);
            return new DocumentSetupListDto
            {
                NonBlockedCustomerCount = nonBlocked,
                Documents = documents
            };
        }

        public async Task<DocumentSetupItemDto> CreateDocumentAsync(
            SaveDocumentNameDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var name = CleanRequired(dto.Name, 150, "Name");
            await EnsureUniqueNameAsync(name, null, cancellationToken);
            var now = DateTime.UtcNow;
            var category = new CustomerDocumentCategory
            {
                Name = name,
                AsksEveryCustomer = false,
                CreatedByUserId = actor.UserId,
                CreatedByName = actor.DisplayName,
                CreatedAt = now
            };
            _context.CustomerDocumentCategories.Add(category);
            // The audit is not tied to the row. Deleting an unused document must not rewrite history,
            // and a category foreign key would be set null on that delete.
            RecordAudit(CustomerDocumentAction.CategoryCreated, actor,
                notes: $"Document '{name}' created. It is not asked until the switch is turned on.");
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new CustomerDocumentConflictException(NameClashMessage, ex);
            }
            return ToSetupItem(category);
        }

        public async Task<DocumentSetupItemDto> RenameDocumentAsync(
            int id,
            SaveDocumentNameDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var name = CleanRequired(dto.Name, 150, "Name");
            await ExecuteResilientlyAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                var category = await EditableCategoryAsync(id, cancellationToken);
                if (string.Equals(category.Name, name, StringComparison.Ordinal))
                    return;

                await EnsureUniqueNameAsync(name, id, cancellationToken);
                var previous = category.Name;
                category.Name = name;
                category.UpdatedAt = DateTime.UtcNow;
                await using var transaction = await BeginSerializableAsync(cancellationToken);
                try
                {
                    var afterId = 0;
                    while (true)
                    {
                        var copies = await _context.CustomerDocumentRequirements
                            .Where(r => r.CategoryId == id && r.Id > afterId)
                            .OrderBy(r => r.Id)
                            .Take(AssignmentBatchSize)
                            .ToListAsync(cancellationToken);
                        if (copies.Count == 0)
                            break;
                        afterId = copies[^1].Id;
                        foreach (var copy in copies)
                            copy.Name = name;
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    RecordAudit(CustomerDocumentAction.CategoryUpdated, actor, category: category,
                        notes: $"Name: {previous} → {name}");
                    await _context.SaveChangesAsync(cancellationToken);
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    if (transaction != null)
                        await transaction.RollbackAsync(CancellationToken.None);
                    throw new CustomerDocumentConflictException(NameClashMessage, ex);
                }
                catch
                {
                    if (transaction != null)
                        await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });

            var saved = await _context.CustomerDocumentCategories.AsNoTracking()
                .SingleAsync(c => c.Id == id, cancellationToken);
            return ToSetupItem(saved);
        }

        public async Task RemoveDocumentAsync(
            int id,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var category = await EditableCategoryAsync(id, cancellationToken);
            var used = await _context.CustomerDocumentRequirements
                .AnyAsync(r => r.CategoryId == id, cancellationToken);
            if (!used)
            {
                var description = $"Unused document '{category.Name}' deleted.";
                _context.CustomerDocumentCategories.Remove(category);
                RecordAudit(CustomerDocumentAction.CategoryDeleted, actor, notes: description);
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            category.IsHidden = true;
            category.AsksEveryCustomer = false;
            category.UpdatedAt = DateTime.UtcNow;
            await WithdrawCopiesWithoutFilesAsync(category, actor, "Removed. Customers who already have a file keep it.", cancellationToken);
            RecordAudit(CustomerDocumentAction.CategoryDeactivated, actor, category: category,
                notes: $"Document '{category.Name}' removed. Customers who already have a file keep it.");
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<DocumentSetupItemDto> SetAsksEveryCustomerAsync(
            int id,
            bool asksEveryCustomer,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            await ExecuteResilientlyAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                var category = await EditableCategoryAsync(id, cancellationToken);
                await using var transaction = await BeginSerializableAsync(cancellationToken);
                try
                {
                    if (asksEveryCustomer)
                        await AskEveryoneAsync(category, actor, cancellationToken);
                    else
                        await StopAskingAsync(category, actor, cancellationToken);
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    if (transaction != null)
                        await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });

            var saved = await _context.CustomerDocumentCategories.AsNoTracking()
                .SingleAsync(c => c.Id == id, cancellationToken);
            return ToSetupItem(saved);
        }


        public async Task<CustomerDocumentChecklistDto> GetChecklistAsync(
            int customerId,
            CancellationToken cancellationToken = default)
        {
            var customer = await _context.Customers.AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => new { c.Id, c.FullName })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Customer not found.");

            var requirements = await _context.CustomerDocumentRequirements
                .AsNoTracking()
                .Where(r => r.CustomerId == customerId)
                .Include(r => r.Category)
                .Include(r => r.Versions.OrderByDescending(v => v.VersionNumber).Take(VersionPreviewSize + 1))
                .OrderByDescending(r => r.IsRequired)
                .ThenBy(r => r.DisplayOrder)
                .ThenBy(r => r.Name)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var visible = requirements.Where(IsVisible).ToList();
            var takenCategoryIds = requirements
                .Where(r => r.CategoryId != null && IsVisible(r))
                .Select(r => r.CategoryId!.Value)
                .ToHashSet();
            var available = await _context.CustomerDocumentCategories.AsNoTracking()
                .Where(c => !c.IsHidden && !c.IsOther && !c.AsksEveryCustomer)
                .OrderBy(c => c.Id)
                .Select(c => new CustomerDocumentTypeOptionDto { CategoryId = c.Id, Name = c.Name })
                .ToListAsync(cancellationToken);

            return new CustomerDocumentChecklistDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName,
                StillNeeded = visible.Count(r => r.IsRequired && r.Status == CustomerDocumentStatus.Needed),
                Done = visible.Count(r => r.Status != CustomerDocumentStatus.Needed),
                Requirements = visible.Select(MapRequirement).ToList(),
                AvailableTypes = available.Where(a => !takenCategoryIds.Contains(a.CategoryId)).ToList()
            };
        }

        // Keyset (cursor) pagination on the monotonic Id: the caller passes the Id of the last row it
        // has seen and receives strictly older rows. Unlike skip/take, this stays stable when new
        // audit rows are appended between page loads — offset paging would repeat or skip rows.
        public async Task<PagedResult<CustomerDocumentAuditDto>> GetHistoryAsync(
            int customerId,
            int? beforeId,
            int take,
            CancellationToken cancellationToken = default)
        {
            if (!await _context.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, cancellationToken))
                throw new KeyNotFoundException("Customer not found.");
            take = Math.Clamp(take, 1, 200);
            var query = _context.CustomerDocumentAuditEntries
                .AsNoTracking()
                .Where(a => a.CustomerId == customerId);
            if (beforeId.HasValue)
                query = query.Where(a => a.Id < beforeId.Value);
            var rows = await query
                .OrderByDescending(a => a.Id)
                .Select(AuditProjection)
                .Take(take + 1)
                .ToListAsync(cancellationToken);
            return new PagedResult<CustomerDocumentAuditDto>
            {
                Items = rows.Take(take).ToList(),
                HasMore = rows.Count > take
            };
        }

        public async Task<PagedResult<CustomerDocumentVersionDto>> GetVersionsAsync(int customerId, int requirementId,
            int? beforeVersionNumber, int take, CancellationToken cancellationToken = default)
        {
            if (!await _context.CustomerDocumentRequirements.AsNoTracking()
                    .AnyAsync(r => r.Id == requirementId && r.CustomerId == customerId, cancellationToken))
                throw new KeyNotFoundException("Document requirement not found for this customer.");
            if (beforeVersionNumber is <= 0)
                throw new InvalidOperationException("The version cursor is invalid.");
            take = Math.Clamp(take, 1, 100);
            var query = _context.CustomerDocumentVersions.AsNoTracking()
                .Where(v => v.RequirementId == requirementId);
            if (beforeVersionNumber.HasValue)
                query = query.Where(v => v.VersionNumber < beforeVersionNumber.Value);
            var rows = await query.OrderByDescending(v => v.VersionNumber).Take(take + 1)
                .ToListAsync(cancellationToken);
            return new PagedResult<CustomerDocumentVersionDto>
            {
                Items = rows.Take(take).Select(MapVersion).ToList(),
                HasMore = rows.Count > take
            };
        }

        public async Task<CustomerDocumentRequirementDto> AddDocumentAsync(
            int customerId,
            int? categoryId,
            string? name,
            CustomerDocumentUpload upload,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var customerExists = await _context.Customers.AsNoTracking()
                .AnyAsync(c => c.Id == customerId, cancellationToken);
            if (!customerExists)
                throw new KeyNotFoundException("Customer not found.");

            string? customName = null;
            string documentName;
            if (categoryId.HasValue)
            {
                var category = await _context.CustomerDocumentCategories.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == categoryId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException("Document type not found.");
                if (category.IsHidden || category.IsOther)
                    throw new InvalidOperationException("That document type is no longer available.");
                documentName = category.Name;
            }
            else
            {
                customName = CleanRequired(name, 150, "Document name");
                documentName = customName;
            }

            var validated = ValidateFile(upload);
            var storedFileName = await _storage.SaveAsync(upload.Content, validated.Extension, cancellationToken);
            var committed = false;
            var attempt = 0;
            int requirementId = 0;
            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    if (attempt++ > 0) _context.ChangeTracker.Clear();
                    await using var transaction = await BeginSerializableAsync(cancellationToken);
                    var customer = await _context.Customers.SingleAsync(c => c.Id == customerId, cancellationToken);
                    var rows = await _context.CustomerDocumentRequirements
                        .Include(r => r.Category)
                        .Include(r => r.Versions)
                        .Where(r => r.CustomerId == customerId)
                        .ToListAsync(cancellationToken);

                    // A type that exists only as a quiet row (not asked, no file) is brought to life;
                    // one the customer can already see is a duplicate.
                    var existing = categoryId.HasValue
                        ? rows.SingleOrDefault(r => r.CategoryId == categoryId.Value)
                        : rows.FirstOrDefault(r => string.Equals(r.Name, documentName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null && IsVisible(existing))
                        throw new CustomerDocumentConflictException("This customer already has that document.");

                    var now = DateTime.UtcNow;
                    var requirement = existing;
                    var previous = existing?.Status;
                    if (requirement == null)
                    {
                        requirement = new CustomerDocumentRequirement
                        {
                            Customer = customer,
                            CustomerId = customer.Id,
                            Name = documentName,
                            IsRequired = false,
                            DisplayOrder = 1000,
                            Status = CustomerDocumentStatus.Needed,
                            CreatedAt = now
                        };
                        _context.CustomerDocumentRequirements.Add(requirement);
                    }
                    else
                    {
                        requirement.IsSuppressed = false;
                        requirement.Name = documentName;
                    }
                    var version = AttachFile(requirement, storedFileName, validated, actor, now);
                    RecordAudit(CustomerDocumentAction.DocumentAdded, actor, customer, requirement,
                        requirement.Category, version, previous, requirement.Status,
                        $"{documentName} added with a file.");
                    try
                    {
                        await SaveWithConcurrencyMessageAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                    {
                        throw new CustomerDocumentConflictException("This customer already has that document.", ex);
                    }
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);
                    requirementId = requirement.Id;
                    committed = true;
                });
                return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
            }
            finally
            {
                if (!committed)
                    await SafeDeleteAsync(storedFileName);
            }
        }

        public async Task<CustomerDocumentRequirementDto> UploadAsync(
            int customerId,
            int requirementId,
            string concurrencyToken,
            CustomerDocumentUpload upload,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            if (!await _context.CustomerDocumentRequirements.AsNoTracking()
                    .AnyAsync(r => r.Id == requirementId && r.CustomerId == customerId, cancellationToken))
                throw new KeyNotFoundException("Document requirement not found.");

            var validated = ValidateFile(upload);

            // Written to storage once, outside the retriable unit below: a retry that re-saved it
            // would leave the first copy orphaned with nothing pointing at it.
            var storedFileName = await _storage.SaveAsync(upload.Content, validated.Extension, cancellationToken);
            var committed = false;
            var attempt = 0;
            try
            {
                await ExecuteResilientlyAsync(async () =>
                {
                    // A retry re-reads and rebuilds everything below, so the failed attempt's pending
                    // version row has to go first — replaying it would leave two current versions.
                    if (attempt++ > 0) _context.ChangeTracker.Clear();
                    await using var transaction = await BeginSerializableAsync(cancellationToken);
                    var requirement = await LoadRequirementForWriteAsync(customerId, requirementId, cancellationToken);
                    ApplyConcurrencyToken(requirement, concurrencyToken);

                    var hadFile = requirement.Versions.Any(v => v.IsCurrent);
                    var previous = requirement.Status;
                    var version = AttachFile(requirement, storedFileName, validated, actor, DateTime.UtcNow);
                    RecordAudit(hadFile ? CustomerDocumentAction.ReplacementUploaded : CustomerDocumentAction.FileUploaded,
                        actor, requirement.Customer, requirement, requirement.Category, version,
                        previous, requirement.Status,
                        hadFile ? "The file was replaced; the old file is kept." : "Document uploaded.");

                    await SaveWithConcurrencyMessageAsync(cancellationToken);
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);
                    committed = true;
                });
                // Read back outside the retriable unit. A transient failure while re-reading must not
                // re-run the insert above — the version is already committed by this point.
                return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
            }
            finally
            {
                if (!committed)
                    await SafeDeleteAsync(storedFileName);
            }
        }

        public async Task<CustomerDocumentRequirementDto> MarkNotNeededAsync(
            int customerId,
            int requirementId,
            NotNeededDocumentDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var reason = CleanRequired(dto.Reason, NotNeededReasonMaxLength, "A reason");
            var requirement = await LoadRequirementForWriteAsync(customerId, requirementId, cancellationToken);
            ApplyConcurrencyToken(requirement, dto.ConcurrencyToken);
            if (requirement.Status != CustomerDocumentStatus.Needed)
                throw new InvalidOperationException("Only a document that is still needed can be marked not needed.");

            var now = DateTime.UtcNow;
            requirement.Status = CustomerDocumentStatus.NotNeeded;
            requirement.NotNeededReason = reason;
            requirement.NotNeededByUserId = actor.UserId;
            requirement.NotNeededByName = actor.DisplayName;
            requirement.NotNeededAt = now;
            Touch(requirement, actor, now);
            RecordAudit(CustomerDocumentAction.MarkedNotNeeded, actor, requirement.Customer, requirement,
                requirement.Category, previousStatus: CustomerDocumentStatus.Needed,
                newStatus: CustomerDocumentStatus.NotNeeded, notes: reason);
            await SaveWithConcurrencyMessageAsync(cancellationToken);
            return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
        }

        public Task<CustomerDocumentDownload> DownloadAsync(
            int customerId,
            int requirementId,
            int versionId,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default) =>
            ReleaseFileAsync(customerId, requirementId, versionId, actor, CustomerDocumentAction.FileDownloaded,
                "downloaded", cancellationToken);

        public Task<CustomerDocumentDownload> ViewAsync(
            int customerId,
            int requirementId,
            int versionId,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default) =>
            ReleaseFileAsync(customerId, requirementId, versionId, actor, CustomerDocumentAction.FileViewed,
                "viewed", cancellationToken);

        private async Task<CustomerDocumentDownload> ReleaseFileAsync(
            int customerId,
            int requirementId,
            int versionId,
            CustomerDocumentActor actor,
            CustomerDocumentAction action,
            string verb,
            CancellationToken cancellationToken)
        {
            var version = await _context.CustomerDocumentVersions
                .Include(v => v.Requirement).ThenInclude(r => r.Customer)
                .Include(v => v.Requirement).ThenInclude(r => r.Category)
                .SingleOrDefaultAsync(v => v.Id == versionId
                                           && v.RequirementId == requirementId
                                           && v.Requirement.CustomerId == customerId, cancellationToken)
                ?? throw new KeyNotFoundException("Document version not found.");

            var content = await _storage.OpenReadAsync(version.StoredFileName, cancellationToken);
            if (content == null)
            {
                _logger.LogError("Customer document version {VersionId} is missing its private stored file.", version.Id);
                throw new FileNotFoundException("The stored document is unavailable. Upload the file again.");
            }

            // Fail closed: a private identity document must never be released without a durable
            // access record. If the audit write fails, dispose the opened stream and surface the
            // error rather than serving an unlogged access to a sensitive file.
            try
            {
                RecordAudit(action, actor, version.Requirement.Customer,
                    version.Requirement, version.Requirement.Category, version,
                    notes: $"File {version.VersionNumber} {verb}.");
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blocked a customer document release because its audit record could not be saved for version {VersionId}.", version.Id);
                await content.DisposeAsync();
                throw;
            }

            return new CustomerDocumentDownload
            {
                Content = content,
                FileName = version.OriginalFileName,
                ContentType = version.ContentType
            };
        }

        private async Task<CustomerDocumentCategory> EditableCategoryAsync(int id, CancellationToken cancellationToken)
        {
            var category = await _context.CustomerDocumentCategories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Document not found.");
            if (category.IsHidden || category.IsOther)
                throw new InvalidOperationException("That document is not on Document setup.");
            return category;
        }

        private async Task EnsureUniqueNameAsync(string name, int? exceptId, CancellationToken cancellationToken)
        {
            var key = name.ToLower();
            var clash = await _context.CustomerDocumentCategories.AnyAsync(
                c => (exceptId == null || c.Id != exceptId) && c.Name.ToLower() == key, cancellationToken);
            if (clash)
                throw new CustomerDocumentConflictException(NameClashMessage);
        }

        private static DocumentSetupItemDto ToSetupItem(CustomerDocumentCategory category) => new()
        {
            Id = category.Id,
            Name = category.Name,
            AsksEveryCustomer = category.AsksEveryCustomer
        };

        private async Task AskEveryoneAsync(
            CustomerDocumentCategory category,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            category.AsksEveryCustomer = true;
            category.UpdatedAt = now;
            var afterId = 0;
            while (true)
            {
                var customers = await _context.Customers
                    .Where(c => c.Status != CustomerStatus.Blocked && c.Id > afterId)
                    .OrderBy(c => c.Id)
                    .Take(AssignmentBatchSize)
                    .ToListAsync(cancellationToken);
                if (customers.Count == 0)
                    break;
                afterId = customers[^1].Id;
                await AskBatchAsync(category, customers, actor, now, cancellationToken);
            }

            RecordAudit(CustomerDocumentAction.BulkCategoryAssignment, actor, category: category,
                notes: $"Ask every customer switched on for '{category.Name}'.");
            await _context.SaveChangesAsync(cancellationToken);
            DetachExcept(category);
        }

        private async Task AskBatchAsync(
            CustomerDocumentCategory category,
            List<Customer> customers,
            CustomerDocumentActor actor,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var ids = customers.Select(c => c.Id).ToArray();
            var rows = await _context.CustomerDocumentRequirements
                .Where(r => r.CategoryId == category.Id && ids.Contains(r.CustomerId))
                .ToListAsync(cancellationToken);
            var rowIds = rows.Select(r => r.Id).ToArray();
            var withFiles = rowIds.Length == 0
                ? []
                : (await _context.CustomerDocumentVersions.AsNoTracking()
                    .Where(v => rowIds.Contains(v.RequirementId))
                    .Select(v => v.RequirementId)
                    .Distinct()
                    .ToListAsync(cancellationToken)).ToHashSet();
            var byCustomer = rows.ToDictionary(r => r.CustomerId);

            foreach (var customer in customers)
            {
                if (!byCustomer.TryGetValue(customer.Id, out var row))
                {
                    var created = CustomerDocumentAssignment.FromCategory(customer, category, actor, now);
                    _context.CustomerDocumentRequirements.Add(created);
                    RecordAudit(CustomerDocumentAction.CategoryAssigned, actor, customer, created, category,
                        newStatus: CustomerDocumentStatus.Needed,
                        notes: $"'{category.Name}' is Needed because it is asked from every customer.");
                    continue;
                }

                if (withFiles.Contains(row.Id))
                {
                    row.IsSuppressed = false;
                    continue;
                }

                if (row.Status == CustomerDocumentStatus.NotNeeded)
                {
                    row.IsSuppressed = false;
                    continue;
                }

                var previous = row.Status;
                var changed = row.IsSuppressed || !row.IsRequired || row.Status != CustomerDocumentStatus.Needed;
                row.Status = CustomerDocumentStatus.Needed;
                row.IsRequired = true;
                row.IsSuppressed = false;
                row.NotNeededReason = null;
                row.NotNeededByUserId = null;
                row.NotNeededByName = null;
                row.NotNeededAt = null;
                row.Name = category.Name;
                Touch(row, actor, now);
                if (changed)
                    RecordAudit(CustomerDocumentAction.CategoryAssigned, actor, customer, row, category,
                        previousStatus: previous, newStatus: CustomerDocumentStatus.Needed,
                        notes: $"'{category.Name}' is Needed because it is asked from every customer.");
            }

            await _context.SaveChangesAsync(cancellationToken);
            DetachExcept(category);
        }

        private async Task StopAskingAsync(
            CustomerDocumentCategory category,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken)
        {
            category.AsksEveryCustomer = false;
            category.UpdatedAt = DateTime.UtcNow;
            await WithdrawCopiesWithoutFilesAsync(category, actor,
                "Removed because the document is no longer asked. Copies with a file stay.", cancellationToken);
            RecordAudit(CustomerDocumentAction.BulkCategoryAssignment, actor, category: category,
                notes: $"Ask every customer switched off for '{category.Name}'.");
            await _context.SaveChangesAsync(cancellationToken);
            DetachExcept(category);
        }

        /// <summary>Copies with no file disappear from customer lists. Copies with a file stay under Done. Rows are not deleted.</summary>
        private async Task WithdrawCopiesWithoutFilesAsync(
            CustomerDocumentCategory category,
            CustomerDocumentActor actor,
            string notes,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var afterId = 0;
            while (true)
            {
                var batch = await _context.CustomerDocumentRequirements
                    .Where(r => r.CategoryId == category.Id && r.Id > afterId)
                    .OrderBy(r => r.Id)
                    .Take(AssignmentBatchSize)
                    .Select(r => new { r.Id, HasFile = r.Versions.Any() })
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0)
                    break;
                afterId = batch[^1].Id;
                var fileless = batch.Where(r => !r.HasFile).Select(r => r.Id).ToArray();
                if (fileless.Length == 0)
                    continue;
                var rows = await _context.CustomerDocumentRequirements
                    .Where(r => fileless.Contains(r.Id))
                    .Include(r => r.Customer)
                    .ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    if (row.IsSuppressed && !row.IsRequired)
                        continue;
                    var previous = row.Status;
                    row.IsSuppressed = true;
                    row.IsRequired = false;
                    Touch(row, actor, now);
                    RecordAudit(CustomerDocumentAction.CategoryUpdated, actor, row.Customer, row, category,
                        previousStatus: previous, newStatus: row.Status, notes: notes);
                }
                await _context.SaveChangesAsync(cancellationToken);
                DetachExcept(category);
            }
        }

        private void DetachExcept(CustomerDocumentCategory category)
        {
            foreach (var entry in _context.ChangeTracker.Entries()
                         .Where(e => !ReferenceEquals(e.Entity, category))
                         .ToList())
                entry.State = EntityState.Detached;
        }

        private async Task<CustomerDocumentRequirement> LoadRequirementForWriteAsync(
            int customerId,
            int requirementId,
            CancellationToken cancellationToken) =>
            await _context.CustomerDocumentRequirements
                .Include(r => r.Customer)
                .Include(r => r.Category)
                .Include(r => r.Versions)
                .SingleOrDefaultAsync(r => r.Id == requirementId && r.CustomerId == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Document requirement not found.");

        private async Task<CustomerDocumentRequirementDto> LoadRequirementAsync(
            int customerId,
            int requirementId,
            CancellationToken cancellationToken)
        {
            var requirement = await _context.CustomerDocumentRequirements.AsNoTracking()
                .Include(r => r.Category)
                .Include(r => r.Versions.OrderByDescending(v => v.VersionNumber).Take(VersionPreviewSize + 1))
                .SingleOrDefaultAsync(r => r.Id == requirementId && r.CustomerId == customerId, cancellationToken)
                ?? throw new KeyNotFoundException("Document requirement not found.");
            return MapRequirement(requirement);
        }

        private static CustomerDocumentRequirementDto MapRequirement(CustomerDocumentRequirement requirement)
        {
            // Versions arrive bounded to the newest VersionPreviewSize (+1 probe) from the read paths;
            // the probe row, if present, only tells us older versions exist and is not rendered.
            var ordered = requirement.Versions.OrderByDescending(v => v.VersionNumber).ToList();
            var hasMoreVersions = ordered.Count > VersionPreviewSize;
            var versions = ordered
                .Take(VersionPreviewSize)
                .Select(MapVersion).ToList();

            return new CustomerDocumentRequirementDto
            {
                Id = requirement.Id,
                CategoryId = requirement.CategoryId,
                Name = requirement.Name,
                Description = requirement.Description,
                IsRequired = requirement.IsRequired,
                DisplayOrder = requirement.DisplayOrder,
                Status = requirement.Status,
                NotNeededReason = requirement.NotNeededReason,
                NotNeededByName = requirement.NotNeededByName,
                NotNeededAt = requirement.NotNeededAt,
                LastActionByName = requirement.LastActionByName,
                UpdatedAt = requirement.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(requirement.RowVersion),
                LatestVersion = versions.SingleOrDefault(v => v.IsCurrent),
                Versions = versions,
                HasMoreVersions = hasMoreVersions
            };
        }

        private static CustomerDocumentVersionDto MapVersion(CustomerDocumentVersion version) => new()
        {
            Id = version.Id,
            VersionNumber = version.VersionNumber,
            IsCurrent = version.IsCurrent,
            OriginalFileName = version.OriginalFileName,
            ContentType = version.ContentType,
            FileSize = version.FileSize,
            UploadedByName = version.UploadedByName,
            UploadedAt = version.UploadedAt
        };

        /// <summary>
        /// A copy shows when it has a file, or it is still asked (Needed) or set aside (Not needed).
        /// A switched-off or removed copy with no file is suppressed and disappears. Nothing is deleted.
        /// </summary>
        private static bool IsVisible(CustomerDocumentRequirement requirement) =>
            requirement.Versions.Count > 0
            || (!requirement.IsSuppressed && (
                requirement.IsRequired
                || requirement.Status != CustomerDocumentStatus.Needed));

        private static ValidatedUpload ValidateFile(CustomerDocumentUpload upload)
        {
            var validated = UploadedFileValidator.Validate(upload.Content, upload.FileName, upload.Length, MaxFileSize);
            if (!SupportedTypes.Contains(validated.Extension))
                throw new InvalidOperationException("Choose a PDF, JPG or PNG file.");
            return validated;
        }

        /// <summary>Makes the new file the current one (the previous file stays as an older file) and marks the document Uploaded.</summary>
        private CustomerDocumentVersion AttachFile(
            CustomerDocumentRequirement requirement,
            string storedFileName,
            ValidatedUpload validated,
            CustomerDocumentActor actor,
            DateTime now)
        {
            var current = requirement.Versions.SingleOrDefault(v => v.IsCurrent);
            if (current != null)
                current.IsCurrent = false;

            var version = new CustomerDocumentVersion
            {
                Requirement = requirement,
                VersionNumber = requirement.Versions.Count == 0 ? 1 : requirement.Versions.Max(v => v.VersionNumber) + 1,
                IsCurrent = true,
                StoredFileName = storedFileName,
                OriginalFileName = validated.OriginalFileName,
                ContentType = validated.ContentType,
                FileSize = validated.FileSize,
                UploadedByUserId = actor.UserId,
                UploadedByName = actor.DisplayName,
                UploadedAt = now
            };
            requirement.Status = CustomerDocumentStatus.Uploaded;
            requirement.NotNeededReason = null;
            requirement.NotNeededByUserId = null;
            requirement.NotNeededByName = null;
            requirement.NotNeededAt = null;
            Touch(requirement, actor, now);
            _context.CustomerDocumentVersions.Add(version);
            return version;
        }

        private void ApplyConcurrencyToken(CustomerDocumentRequirement requirement, string token) =>
            ApplyConcurrencyToken(requirement.RowVersion, token,
                expected => _context.Entry(requirement).Property(r => r.RowVersion).OriginalValue = expected);

        private static void ApplyConcurrencyToken(byte[] current, string token, Action<byte[]> setOriginal)
        {
            byte[] expected;
            try { expected = Convert.FromBase64String(token ?? string.Empty); }
            catch (FormatException) { throw new InvalidOperationException("The item version is invalid. Refresh and retry."); }
            if (current.Length > 0 && !current.SequenceEqual(expected))
                throw new DbUpdateConcurrencyException("This item changed in another browser. Refresh and retry.");
            if (expected.Length > 0)
                setOriginal(expected);
        }

        private async Task SaveWithConcurrencyMessageAsync(CancellationToken cancellationToken)
        {
            try { await _context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new DbUpdateConcurrencyException("This document changed in another browser. Refresh before trying again.", ex);
            }
        }

        private async Task<IDbContextTransaction?> BeginSerializableAsync(CancellationToken cancellationToken) =>
            _context.Database.IsRelational() && _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

        /// <summary>
        /// Wraps one <see cref="BeginSerializableAsync"/> unit so it can actually run.
        /// <para>
        /// SQL Server is registered with <c>EnableRetryOnFailure</c>, and EF refuses to execute any
        /// operation inside a transaction the caller opened itself unless the whole unit goes through
        /// the retrying strategy — it throws "the configured execution strategy
        /// 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions". Every
        /// path below that opens its own transaction therefore has to be a retriable unit; without
        /// this they failed outright on the real database. Nested calls are a pass-through, because
        /// EF suspends the strategy for the duration of an outer execution.
        /// </para>
        /// </summary>
        private Task ExecuteResilientlyAsync(Func<Task> operation) =>
            _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

        private async Task SafeDeleteAsync(string storedFileName)
        {
            try { await _storage.DeleteAsync(storedFileName, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not clean up uncommitted customer document file {StoredFileName}.", storedFileName);
            }
        }

        private void RecordAudit(
            CustomerDocumentAction action,
            CustomerDocumentActor actor,
            Customer? customer = null,
            CustomerDocumentRequirement? requirement = null,
            CustomerDocumentCategory? category = null,
            CustomerDocumentVersion? version = null,
            CustomerDocumentStatus? previousStatus = null,
            CustomerDocumentStatus? newStatus = null,
            string? notes = null)
        {
            _context.CustomerDocumentAuditEntries.Add(new CustomerDocumentAuditEntry
            {
                Customer = customer,
                CustomerId = customer?.Id,
                Requirement = requirement,
                RequirementId = requirement?.Id,
                Category = category,
                CategoryId = category?.Id,
                Version = version,
                VersionId = version?.Id,
                Action = action,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                // Not truncated: a category change summary records exact before/after values for every
                // control field. The column is unbounded, so the full audited detail is preserved.
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                PerformedByUserId = actor.UserId,
                PerformedByName = CleanRequired(actor.DisplayName, 200, "Actor name"),
                OccurredAt = DateTime.UtcNow
            });
        }

        private static void Touch(CustomerDocumentRequirement requirement, CustomerDocumentActor actor, DateTime now)
        {
            requirement.LastActionByUserId = actor.UserId;
            requirement.LastActionByName = actor.DisplayName;
            requirement.UpdatedAt = now;
        }

        private static string CleanRequired(string? value, int maxLength, string label)
        {
            var cleaned = value?.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                throw new InvalidOperationException($"{label} is required.");
            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
        }

        private static bool IsUniqueViolation(DbUpdateException exception) =>
            exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
