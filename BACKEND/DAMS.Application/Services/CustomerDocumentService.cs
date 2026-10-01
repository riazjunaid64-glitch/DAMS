using System.Data;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
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
        private const string OtherCategoryCode = "other";
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

        public async Task<List<CustomerDocumentCategoryDto>> GetCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default)
        {
            var query = _context.CustomerDocumentCategories.AsNoTracking().AsQueryable();
            if (!includeInactive)
                query = query.Where(c => c.IsActive);

            var rows = await query
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => new
                {
                    Category = c,
                    UsageCount = c.Requirements.Count,
                })
                .ToListAsync(cancellationToken);

            return rows.Select(row => new CustomerDocumentCategoryDto
            {
                Id = row.Category.Id,
                Name = row.Category.Name,
                Code = row.Category.Code,
                Description = row.Category.Description,
                IsRequiredByDefault = row.Category.IsRequiredByDefault,
                DisplayOrder = row.Category.DisplayOrder,
                IsActive = row.Category.IsActive,
                AssignToNewCustomers = row.Category.AssignToNewCustomers,
                UsageCount = row.UsageCount,
                CreatedAt = row.Category.CreatedAt,
                UpdatedAt = row.Category.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(row.Category.RowVersion)
            }).ToList();
        }

        public async Task<CustomerDocumentCategoryDto> CreateCategoryAsync(
            CreateCustomerDocumentCategoryDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var values = ValidateCategory(dto.Name, dto.Code, dto.Description);

            if (await _context.CustomerDocumentCategories.AnyAsync(c => c.Code == values.Code, cancellationToken))
                throw new InvalidOperationException($"A document category with code '{values.Code}' already exists.");

            ValidateAssignment(dto.AssignmentMode, dto.SelectedCustomerIds);
            var now = DateTime.UtcNow;
            var attempt = 0;
            CustomerDocumentCategory category = null!;

            await ExecuteResilientlyAsync(async () =>
            {
                // Each attempt starts clean and builds its own row. A retry cannot reuse the previous
                // attempt's entity: that insert was rolled back with its transaction, but EF still
                // holds the key it had been given, so re-saving it would try to insert an explicit
                // identity value. Rebuilding is also what keeps the audit entry one-per-row.
                if (attempt++ > 0) _context.ChangeTracker.Clear();
                category = new CustomerDocumentCategory
                {
                    Name = values.Name,
                    Code = values.Code,
                    Description = values.Description,
                    IsRequiredByDefault = dto.IsRequiredByDefault,
                    DisplayOrder = dto.DisplayOrder,
                    IsActive = true,
                    AssignToNewCustomers = dto.AssignmentMode == CustomerDocumentAssignmentMode.NewCustomersOnly,
                    CreatedByUserId = actor.UserId,
                    CreatedByName = actor.DisplayName,
                    CreatedAt = now
                };
                await using var transaction = await BeginSerializableAsync(cancellationToken);
                try
                {
                    _context.CustomerDocumentCategories.Add(category);
                    RecordAudit(CustomerDocumentAction.CategoryCreated, actor, category: category,
                        notes: $"Category '{category.Name}' created with assignment mode {dto.AssignmentMode}.");
                    await _context.SaveChangesAsync(cancellationToken);

                    if (dto.AssignmentMode is CustomerDocumentAssignmentMode.AllActiveCustomers
                        or CustomerDocumentAssignmentMode.SelectedCustomers)
                    {
                        await AssignCategoryAsync(category.Id, new AssignCustomerDocumentCategoryDto
                        {
                            AssignmentMode = dto.AssignmentMode,
                            SelectedCustomerIds = dto.SelectedCustomerIds
                        }, actor, cancellationToken);
                    }

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

            return await LoadCategoryAsync(category.Id, cancellationToken);
        }

        public async Task<CustomerDocumentCategoryDto> UpdateCategoryAsync(
            int id,
            UpdateCustomerDocumentCategoryDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var category = await _context.CustomerDocumentCategories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Document category not found.");
            ApplyConcurrencyToken(category, dto.ConcurrencyToken);

            var values = ValidateCategory(dto.Name, dto.Code, dto.Description);
            var used = await _context.CustomerDocumentRequirements.AnyAsync(r => r.CategoryId == id, cancellationToken);
            if (used && !string.Equals(category.Code, values.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A category code cannot change after the category has been assigned.");
            if (await _context.CustomerDocumentCategories.AnyAsync(c => c.Id != id && c.Code == values.Code, cancellationToken))
                throw new InvalidOperationException($"A document category with code '{values.Code}' already exists.");

            var wasActive = category.IsActive;
            // Snapshot the checklist-policy fields before the overwrite so the audit records exactly
            // which control values changed; a bare "updated" note is not reconstructable.
            var before = CategorySnapshot(category);
            category.Name = values.Name;
            category.Code = values.Code;
            category.Description = values.Description;
            category.IsRequiredByDefault = dto.IsRequiredByDefault;
            category.DisplayOrder = dto.DisplayOrder;
            category.IsActive = dto.IsActive;
            category.AssignToNewCustomers = dto.IsActive && dto.AssignToNewCustomers;
            category.UpdatedAt = DateTime.UtcNow;
            var changeSummary = DiffSnapshots(before, CategorySnapshot(category));

            var action = wasActive == category.IsActive
                ? CustomerDocumentAction.CategoryUpdated
                : category.IsActive ? CustomerDocumentAction.CategoryActivated : CustomerDocumentAction.CategoryDeactivated;
            RecordAudit(action, actor, category: category, notes: $"Category '{category.Name}' updated. {changeSummary}");

            await SaveWithConcurrencyMessageAsync(cancellationToken);
            return await LoadCategoryAsync(category.Id, cancellationToken);
        }

        public async Task DeleteCategoryAsync(
            int id,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var category = await _context.CustomerDocumentCategories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Document category not found.");

            if (await _context.CustomerDocumentRequirements.AnyAsync(r => r.CategoryId == id, cancellationToken))
                throw new InvalidOperationException("A used category cannot be deleted. Make it inactive instead.");

            var description = $"Unused category '{category.Name}' ({category.Code}) deleted.";
            _context.CustomerDocumentCategories.Remove(category);
            RecordAudit(CustomerDocumentAction.CategoryDeleted, actor, notes: description);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<CustomerDocumentAssignmentResultDto> AssignCategoryAsync(
            int id,
            AssignCustomerDocumentCategoryDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            ValidateAssignment(dto.AssignmentMode, dto.SelectedCustomerIds);
            var category = await _context.CustomerDocumentCategories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Document category not found.");

            if (!category.IsActive)
                throw new InvalidOperationException("An inactive category cannot be assigned to customers.");

            if (dto.AssignmentMode is CustomerDocumentAssignmentMode.NewCustomersOnly
                or CustomerDocumentAssignmentMode.None)
            {
                category.AssignToNewCustomers = dto.AssignmentMode == CustomerDocumentAssignmentMode.NewCustomersOnly;
                category.UpdatedAt = DateTime.UtcNow;
                RecordAudit(CustomerDocumentAction.BulkCategoryAssignment, actor, category: category,
                    notes: category.AssignToNewCustomers
                        ? "Category will be assigned to new customers only."
                        : "Automatic assignment disabled.");
                await _context.SaveChangesAsync(cancellationToken);
                return new CustomerDocumentAssignmentResultDto();
            }

            // The four choices are deliberately exclusive: assigning an existing population
            // does not silently opt future customers into the category.
            category.AssignToNewCustomers = false;

            CustomerDocumentAssignmentResultDto result = null!;
            await ExecuteResilientlyAsync(async () =>
            {
                // Rebuilt on each attempt: the counters below accumulate as batches are assigned, so
                // a retry must start its tally from zero rather than add to a half-finished one.
                result = new CustomerDocumentAssignmentResultDto();
                await using var transaction = await BeginSerializableAsync(cancellationToken);
                try
                {
                    if (dto.AssignmentMode == CustomerDocumentAssignmentMode.AllActiveCustomers)
                    {
                        result.EligibleCustomers = await _context.Customers
                            .CountAsync(c => c.Status == CustomerStatus.Active, cancellationToken);

                        var afterId = 0;
                        while (true)
                        {
                            var customers = await _context.Customers
                                .Where(c => c.Status == CustomerStatus.Active && c.Id > afterId)
                                .OrderBy(c => c.Id)
                                .Take(AssignmentBatchSize)
                                .ToListAsync(cancellationToken);
                            if (customers.Count == 0)
                                break;
                            afterId = customers[^1].Id;
                            result.AssignedCustomers += await AssignBatchAsync(category, customers, actor, cancellationToken);
                        }
                    }
                    else
                    {
                        var selected = dto.SelectedCustomerIds.Distinct().ToArray();
                        foreach (var ids in selected.Chunk(AssignmentBatchSize))
                        {
                            var customers = await _context.Customers
                                .Where(c => ids.Contains(c.Id))
                                .OrderBy(c => c.Id)
                                .ToListAsync(cancellationToken);
                            result.EligibleCustomers += customers.Count;
                            result.AssignedCustomers += await AssignBatchAsync(category, customers, actor, cancellationToken);
                        }
                    }

                    result.AlreadyAssignedCustomers = result.EligibleCustomers - result.AssignedCustomers;
                    RecordAudit(CustomerDocumentAction.BulkCategoryAssignment, actor, category: category,
                        notes: $"Assigned to {result.AssignedCustomers} customer(s); {result.AlreadyAssignedCustomers} already assigned.");
                    category.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    if (transaction != null)
                        await transaction.RollbackAsync(CancellationToken.None);
                    throw new InvalidOperationException("The category assignment conflicted with another request. No duplicate was created; refresh and retry.", ex);
                }
            });

            return result;
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
                // "Asked from every customer" is a category that is both required and handed to every new customer.
                .Where(c => c.IsActive && !(c.IsRequiredByDefault && c.AssignToNewCustomers) && c.Code != OtherCategoryCode)
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
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
                if (!category.IsActive)
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

        private async Task<int> AssignBatchAsync(
            CustomerDocumentCategory category,
            List<Customer> customers,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken)
        {
            if (customers.Count == 0)
                return 0;
            var ids = customers.Select(c => c.Id).ToArray();
            var existingRows = await _context.CustomerDocumentRequirements
                .Where(r => r.CategoryId == category.Id && ids.Contains(r.CustomerId))
                .Select(r => r.CustomerId)
                .ToListAsync(cancellationToken);
            var existing = existingRows.ToHashSet();
            var now = DateTime.UtcNow;
            var count = 0;
            foreach (var customer in customers.Where(c => !existing.Contains(c.Id)))
            {
                var requirement = CustomerDocumentAssignment.FromCategory(customer, category, actor, now);
                _context.CustomerDocumentRequirements.Add(requirement);
                RecordAudit(CustomerDocumentAction.CategoryAssigned, actor, customer, requirement, category,
                    newStatus: CustomerDocumentStatus.Needed, notes: "Assigned by a confirmed bulk action.");
                count++;
            }
            if (count > 0)
                await _context.SaveChangesAsync(cancellationToken);
            foreach (var entry in _context.ChangeTracker.Entries()
                         .Where(e => !ReferenceEquals(e.Entity, category))
                         .ToList())
                entry.State = EntityState.Detached;
            return count;
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

        private async Task<int> RequirementIdAsync(int customerId, int categoryId, CancellationToken cancellationToken) =>
            await _context.CustomerDocumentRequirements.AsNoTracking()
                .Where(r => r.CustomerId == customerId && r.CategoryId == categoryId)
                .Select(r => r.Id)
                .SingleAsync(cancellationToken);

        private async Task<CustomerDocumentCategoryDto> LoadCategoryAsync(int id, CancellationToken cancellationToken) =>
            (await GetCategoriesAsync(true, cancellationToken)).Single(c => c.Id == id);

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

        /// <summary>A document shows on the customer's checklist when it was asked from them, has a file, or was set aside.</summary>
        private static bool IsVisible(CustomerDocumentRequirement requirement) =>
            requirement.IsRequired
            || requirement.Status != CustomerDocumentStatus.Needed
            || requirement.Versions.Count > 0;

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

        private void ApplyConcurrencyToken(CustomerDocumentCategory category, string token) =>
            ApplyConcurrencyToken(category.RowVersion, token,
                expected => _context.Entry(category).Property(c => c.RowVersion).OriginalValue = expected);

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

        private static readonly (string Label, Func<CustomerDocumentCategory, string?> Value)[] CategoryFields =
        {
            ("Name", c => c.Name),
            ("Code", c => c.Code),
            ("Description", c => c.Description),
            ("IsRequiredByDefault", c => c.IsRequiredByDefault.ToString()),
            ("DisplayOrder", c => c.DisplayOrder.ToString()),
            ("IsActive", c => c.IsActive.ToString()),
            ("AssignToNewCustomers", c => c.AssignToNewCustomers.ToString()),
        };

        private static List<(string Label, string? Value)> CategorySnapshot(CustomerDocumentCategory c) =>
            CategoryFields.Select(f => (f.Label, f.Value(c))).ToList();

        private static string DiffSnapshots(
            List<(string Label, string? Value)> before,
            List<(string Label, string? Value)> after)
        {
            var changes = new List<string>();
            for (var i = 0; i < before.Count; i++)
                if (!string.Equals(before[i].Value, after[i].Value, StringComparison.Ordinal))
                    changes.Add($"{before[i].Label}: {before[i].Value ?? "—"} → {after[i].Value ?? "—"}");
            return changes.Count == 0 ? "No fields changed." : string.Join("; ", changes);
        }

        private static void Touch(CustomerDocumentRequirement requirement, CustomerDocumentActor actor, DateTime now)
        {
            requirement.LastActionByUserId = actor.UserId;
            requirement.LastActionByName = actor.DisplayName;
            requirement.UpdatedAt = now;
        }

        private static (string Name, string Code, string? Description) ValidateCategory(
            string name,
            string code,
            string? description)
        {
            var normalizedCode = CleanRequired(code, 80, "Category code").ToLowerInvariant();
            if (!Regex.IsMatch(normalizedCode, "^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Category code may contain lowercase letters, numbers, underscores, and hyphens only.");
            return (CleanRequired(name, 150, "Category name"), normalizedCode, CleanOptional(description, 1000));
        }

        private static void ValidateAssignment(CustomerDocumentAssignmentMode mode, List<int>? selected)
        {
            if (!Enum.IsDefined(mode))
                throw new InvalidOperationException("The category assignment choice is invalid.");
            if (mode == CustomerDocumentAssignmentMode.SelectedCustomers && (selected == null || selected.Count == 0))
                throw new InvalidOperationException("Choose at least one customer for selected-customer assignment.");
            if ((selected?.Distinct().Count() ?? 0) > 5000)
                throw new InvalidOperationException("Select at most 5000 customers in one action.");
        }

        private static string CleanRequired(string? value, int maxLength, string label)
        {
            var cleaned = value?.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                throw new InvalidOperationException($"{label} is required.");
            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
        }

        private static string? CleanOptional(string? value, int maxLength)
        {
            var cleaned = value?.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return null;
            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
        }

        private static bool IsUniqueViolation(DbUpdateException exception) =>
            exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
