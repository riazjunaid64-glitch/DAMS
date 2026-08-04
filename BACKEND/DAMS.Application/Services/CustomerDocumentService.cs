using System.Data;
using System.Text.RegularExpressions;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
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
        public const long MaxFileSize = 25 * 1024 * 1024;
        public const long MaxRequestSize = MaxFileSize + (512 * 1024);
        private const int AssignmentBatchSize = 500;
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
                AllowedFileTypes = SplitTypes(row.Category.AllowedFileTypes),
                MaxFileSizeBytes = row.Category.MaxFileSizeBytes,
                IsActive = row.Category.IsActive,
                AssignToNewCustomers = row.Category.AssignToNewCustomers,
                DefaultDueDays = row.Category.DefaultDueDays,
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
            var values = ValidateCategory(dto.Name, dto.Code, dto.Description, dto.AllowedFileTypes,
                dto.MaxFileSizeBytes, dto.DefaultDueDays);

            if (await _context.CustomerDocumentCategories.AnyAsync(c => c.Code == values.Code, cancellationToken))
                throw new InvalidOperationException($"A document category with code '{values.Code}' already exists.");

            ValidateAssignment(dto.AssignmentMode, dto.SelectedCustomerIds);
            var now = DateTime.UtcNow;
            var category = new CustomerDocumentCategory
            {
                Name = values.Name,
                Code = values.Code,
                Description = values.Description,
                IsRequiredByDefault = dto.IsRequiredByDefault,
                DisplayOrder = dto.DisplayOrder,
                AllowedFileTypes = values.AllowedTypes,
                MaxFileSizeBytes = dto.MaxFileSizeBytes,
                IsActive = true,
                AssignToNewCustomers = dto.AssignmentMode == CustomerDocumentAssignmentMode.NewCustomersOnly,
                DefaultDueDays = dto.DefaultDueDays,
                CreatedByUserId = actor.UserId,
                CreatedByName = actor.DisplayName,
                CreatedAt = now
            };

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

            var values = ValidateCategory(dto.Name, dto.Code, dto.Description, dto.AllowedFileTypes,
                dto.MaxFileSizeBytes, dto.DefaultDueDays);
            var used = await _context.CustomerDocumentRequirements.AnyAsync(r => r.CategoryId == id, cancellationToken);
            if (used && !string.Equals(category.Code, values.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A category code cannot change after the category has been assigned.");
            if (await _context.CustomerDocumentCategories.AnyAsync(c => c.Id != id && c.Code == values.Code, cancellationToken))
                throw new InvalidOperationException($"A document category with code '{values.Code}' already exists.");

            var wasActive = category.IsActive;
            category.Name = values.Name;
            category.Code = values.Code;
            category.Description = values.Description;
            category.IsRequiredByDefault = dto.IsRequiredByDefault;
            category.DisplayOrder = dto.DisplayOrder;
            category.AllowedFileTypes = values.AllowedTypes;
            category.MaxFileSizeBytes = dto.MaxFileSizeBytes;
            category.IsActive = dto.IsActive;
            category.AssignToNewCustomers = dto.IsActive && dto.AssignToNewCustomers;
            category.DefaultDueDays = dto.DefaultDueDays;
            category.UpdatedAt = DateTime.UtcNow;

            var action = wasActive == category.IsActive
                ? CustomerDocumentAction.CategoryUpdated
                : category.IsActive ? CustomerDocumentAction.CategoryActivated : CustomerDocumentAction.CategoryDeactivated;
            RecordAudit(action, actor, category: category, notes: $"Category '{category.Name}' updated.");

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

            var result = new CustomerDocumentAssignmentResultDto();
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
                .Include(r => r.Versions)
                .OrderByDescending(r => r.IsRequired)
                .ThenBy(r => r.DisplayOrder)
                .ThenBy(r => r.Name)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var history = await _context.CustomerDocumentAuditEntries
                .AsNoTracking()
                .Where(a => a.CustomerId == customerId)
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Select(a => new CustomerDocumentAuditDto
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
                })
                .ToListAsync(cancellationToken);

            return new CustomerDocumentChecklistDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.FullName,
                Summary = CustomerDocumentCompletion.Calculate(requirements),
                Requirements = requirements.Select(MapRequirement).ToList(),
                History = history
            };
        }

        public async Task<CustomerDocumentRequirementDto> AddRequirementAsync(
            int customerId,
            AddCustomerDocumentRequirementDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                ?? throw new KeyNotFoundException("Customer not found.");

            if (dto.CategoryId.HasValue)
            {
                var category = await _context.CustomerDocumentCategories
                    .FirstOrDefaultAsync(c => c.Id == dto.CategoryId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException("Document category not found.");
                if (!category.IsActive)
                    throw new InvalidOperationException("An inactive category cannot be assigned.");
                return await AddRequirementFromCategoryAsync(customer, category, dto.IsRequired, dto.DueDate, actor, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException("A custom requirement name is required.");

            if (dto.SaveAsGlobalCategory)
            {
                if (string.IsNullOrWhiteSpace(dto.GlobalCategoryCode))
                    throw new InvalidOperationException("A stable category code is required when saving globally.");

                var selected = dto.SelectedCustomerIds.ToList();
                if (dto.GlobalAssignmentMode == CustomerDocumentAssignmentMode.SelectedCustomers && !selected.Contains(customerId))
                    selected.Add(customerId);

                var category = await CreateCategoryAsync(new CreateCustomerDocumentCategoryDto
                {
                    Name = dto.Name,
                    Code = dto.GlobalCategoryCode,
                    Description = dto.Description,
                    IsRequiredByDefault = dto.IsRequired,
                    DisplayOrder = 1000,
                    AllowedFileTypes = dto.AllowedFileTypes,
                    MaxFileSizeBytes = dto.MaxFileSizeBytes,
                    AssignmentMode = dto.GlobalAssignmentMode,
                    SelectedCustomerIds = selected
                }, actor, cancellationToken);

                var existing = await _context.CustomerDocumentRequirements
                    .AsNoTracking()
                    .AnyAsync(r => r.CustomerId == customerId && r.CategoryId == category.Id, cancellationToken);
                if (existing)
                    return await LoadRequirementAsync(customerId, await RequirementIdAsync(customerId, category.Id, cancellationToken), cancellationToken);

                customer = await _context.Customers.FirstAsync(c => c.Id == customerId, cancellationToken);
                var trackedCategory = await _context.CustomerDocumentCategories.FindAsync([category.Id], cancellationToken)
                    ?? throw new KeyNotFoundException("Document category not found.");
                return await AddRequirementFromCategoryAsync(customer, trackedCategory, dto.IsRequired, dto.DueDate, actor, cancellationToken);
            }

            var customName = CleanRequired(dto.Name, 150, "Requirement name");
            if (await _context.CustomerDocumentRequirements.AnyAsync(
                    r => r.CustomerId == customerId && r.CategoryId == null && r.Name == customName,
                    cancellationToken))
                throw new InvalidOperationException("This customer already has a custom requirement with that name.");

            var types = NormalizeAllowedTypes(dto.AllowedFileTypes);
            ValidateMaxFileSize(dto.MaxFileSizeBytes);
            var now = DateTime.UtcNow;
            var requirement = new CustomerDocumentRequirement
            {
                Customer = customer,
                CustomerId = customer.Id,
                Name = customName,
                Description = CleanOptional(dto.Description, 1000),
                IsRequired = dto.IsRequired,
                DisplayOrder = 1000,
                AllowedFileTypes = types,
                MaxFileSizeBytes = dto.MaxFileSizeBytes,
                Status = CustomerDocumentStatus.Missing,
                DueDate = dto.DueDate,
                LastActionByUserId = actor.UserId,
                LastActionByName = actor.DisplayName,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.CustomerDocumentRequirements.Add(requirement);
            RecordAudit(CustomerDocumentAction.RequirementCreated, actor, customer: customer,
                requirement: requirement, newStatus: requirement.Status, notes: "One-customer custom requirement created.");
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new InvalidOperationException("This customer already has a custom requirement with that name.", ex);
            }
            return await LoadRequirementAsync(customerId, requirement.Id, cancellationToken);
        }

        public async Task<CustomerDocumentRequirementDto> UploadAsync(
            int customerId,
            int requirementId,
            string concurrencyToken,
            CustomerDocumentUpload upload,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var metadata = await _context.CustomerDocumentRequirements.AsNoTracking()
                .Where(r => r.Id == requirementId && r.CustomerId == customerId)
                .Select(r => new { r.AllowedFileTypes, r.MaxFileSizeBytes })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Document requirement not found.");

            var validated = UploadedFileValidator.Validate(upload.Content, upload.FileName, upload.Length, metadata.MaxFileSizeBytes);
            if (!SplitTypes(metadata.AllowedFileTypes).Contains(validated.Extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"This requirement accepts only {metadata.AllowedFileTypes} files.");

            var storedFileName = await _storage.SaveAsync(upload.Content, validated.Extension, cancellationToken);
            var committed = false;
            await using var transaction = await BeginSerializableAsync(cancellationToken);
            try
            {
                var requirement = await LoadRequirementForWriteAsync(customerId, requirementId, cancellationToken);
                ApplyConcurrencyToken(requirement, concurrencyToken);
                EnsureUploadAllowed(requirement.Status);

                var current = requirement.Versions.SingleOrDefault(v => v.IsCurrent);
                if (current != null)
                    current.IsCurrent = false;

                var now = DateTime.UtcNow;
                var version = new CustomerDocumentVersion
                {
                    Requirement = requirement,
                    RequirementId = requirement.Id,
                    VersionNumber = requirement.Versions.Count == 0 ? 1 : requirement.Versions.Max(v => v.VersionNumber) + 1,
                    IsCurrent = true,
                    StoredFileName = storedFileName,
                    OriginalFileName = validated.OriginalFileName,
                    ContentType = validated.ContentType,
                    FileSize = validated.FileSize,
                    UploadedByUserId = actor.UserId,
                    UploadedByName = actor.DisplayName,
                    UploadedAt = now,
                    ReviewStatus = CustomerDocumentVersionStatus.UnderReview
                };
                var previous = requirement.Status;
                requirement.Status = CustomerDocumentStatus.UnderReview;
                requirement.PostponedUntil = null;
                Touch(requirement, actor, now);
                _context.CustomerDocumentVersions.Add(version);
                RecordAudit(current == null ? CustomerDocumentAction.FileUploaded : CustomerDocumentAction.ReplacementUploaded,
                    actor, requirement.Customer, requirement, requirement.Category, version,
                    previous, requirement.Status, current == null ? "Document uploaded for review." : "A new document version was uploaded for review.");

                await SaveWithConcurrencyMessageAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);
                committed = true;
                return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
            }
            finally
            {
                if (!committed)
                    await SafeDeleteAsync(storedFileName);
            }
        }

        public async Task<CustomerDocumentRequirementDto> ChangeStatusAsync(
            int customerId,
            int requirementId,
            CustomerDocumentStatusChangeDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var requirement = await LoadRequirementForWriteAsync(customerId, requirementId, cancellationToken);
            ApplyConcurrencyToken(requirement, dto.ConcurrencyToken);
            var previous = requirement.Status;
            var currentVersion = requirement.Versions.SingleOrDefault(v => v.IsCurrent);
            var reason = CleanOptional(dto.Reason, 2000);
            var now = DateTime.UtcNow;
            CustomerDocumentAction action;

            switch (dto.Status)
            {
                case CustomerDocumentStatus.Requested:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.Missing, CustomerDocumentStatus.Postponed);
                    action = CustomerDocumentAction.Requested;
                    requirement.PostponedUntil = null;
                    break;
                case CustomerDocumentStatus.Approved:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.UnderReview);
                    currentVersion = RequireCurrentVersion(currentVersion);
                    ApplyReview(currentVersion, CustomerDocumentVersionStatus.Approved, actor, now, reason);
                    action = CustomerDocumentAction.Approved;
                    break;
                case CustomerDocumentStatus.Rejected:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.UnderReview);
                    reason = RequireReason(reason, "A rejection reason is required.");
                    currentVersion = RequireCurrentVersion(currentVersion);
                    ApplyReview(currentVersion, CustomerDocumentVersionStatus.Rejected, actor, now, reason);
                    action = CustomerDocumentAction.Rejected;
                    break;
                case CustomerDocumentStatus.ReplacementRequired:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.UnderReview, CustomerDocumentStatus.Rejected,
                        CustomerDocumentStatus.Approved, CustomerDocumentStatus.Expired);
                    reason = RequireReason(reason, "A replacement reason is required.");
                    // A rejected version keeps its original rejection decision and reason.
                    // The later replacement request is a requirement-level audit event.
                    if (currentVersion != null && previous != CustomerDocumentStatus.Rejected)
                        ApplyReview(currentVersion, CustomerDocumentVersionStatus.ReplacementRequired, actor, now, reason);
                    action = CustomerDocumentAction.ReplacementRequested;
                    break;
                case CustomerDocumentStatus.Postponed:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.Missing, CustomerDocumentStatus.Requested,
                        CustomerDocumentStatus.Rejected, CustomerDocumentStatus.ReplacementRequired, CustomerDocumentStatus.Expired);
                    reason = RequireReason(reason, "A postponement reason is required.");
                    if (dto.PostponedUntil.HasValue && dto.PostponedUntil.Value <= now)
                        throw new InvalidOperationException("The collection date must be in the future.");
                    requirement.PostponedUntil = dto.PostponedUntil;
                    action = CustomerDocumentAction.Postponed;
                    break;
                case CustomerDocumentStatus.Waived:
                    EnsureOverrideAllowed(previous);
                    reason = RequireReason(reason, "A waiver reason is required.");
                    requirement.PostponedUntil = null;
                    action = CustomerDocumentAction.Waived;
                    break;
                case CustomerDocumentStatus.NotApplicable:
                    EnsureOverrideAllowed(previous);
                    reason = RequireReason(reason, "A not-applicable reason is required.");
                    requirement.PostponedUntil = null;
                    action = CustomerDocumentAction.MarkedNotApplicable;
                    break;
                case CustomerDocumentStatus.Expired:
                    EnsureCurrentIs(previous, CustomerDocumentStatus.Approved);
                    reason = RequireReason(reason, "An expiry reason is required.");
                    action = CustomerDocumentAction.MarkedExpired;
                    break;
                default:
                    throw new InvalidOperationException("That document status cannot be selected directly.");
            }

            requirement.Status = dto.Status;
            Touch(requirement, actor, now);
            RecordAudit(action, actor, requirement.Customer, requirement, requirement.Category, currentVersion,
                previous, requirement.Status, reason);
            await SaveWithConcurrencyMessageAsync(cancellationToken);
            return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
        }

        public async Task<CustomerDocumentRequirementDto> ChangeDueDateAsync(
            int customerId,
            int requirementId,
            CustomerDocumentDueDateDto dto,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
        {
            var requirement = await LoadRequirementForWriteAsync(customerId, requirementId, cancellationToken);
            ApplyConcurrencyToken(requirement, dto.ConcurrencyToken);
            var oldDate = requirement.DueDate;
            requirement.DueDate = dto.DueDate;
            Touch(requirement, actor, DateTime.UtcNow);
            var notes = $"Due date changed from {FormatDate(oldDate)} to {FormatDate(dto.DueDate)}.";
            if (!string.IsNullOrWhiteSpace(dto.Reason))
                notes += $" {CleanOptional(dto.Reason, 1500)}";
            RecordAudit(CustomerDocumentAction.DueDateChanged, actor, requirement.Customer, requirement,
                requirement.Category, notes: notes);
            await SaveWithConcurrencyMessageAsync(cancellationToken);
            return await LoadRequirementAsync(customerId, requirementId, cancellationToken);
        }

        public async Task<CustomerDocumentDownload> DownloadAsync(
            int customerId,
            int requirementId,
            int versionId,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken = default)
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
                throw new FileNotFoundException("The stored document is unavailable. Ask an administrator to upload a replacement.");
            }

            try
            {
                RecordAudit(CustomerDocumentAction.FileDownloaded, actor, version.Requirement.Customer,
                    version.Requirement, version.Requirement.Category, version,
                    notes: $"Version {version.VersionNumber} downloaded.");
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record download audit for customer document version {VersionId}.", version.Id);
            }

            return new CustomerDocumentDownload
            {
                Content = content,
                FileName = version.OriginalFileName,
                ContentType = version.ContentType
            };
        }

        private async Task<CustomerDocumentRequirementDto> AddRequirementFromCategoryAsync(
            Customer customer,
            CustomerDocumentCategory category,
            bool required,
            DateTime? dueDate,
            CustomerDocumentActor actor,
            CancellationToken cancellationToken)
        {
            if (await _context.CustomerDocumentRequirements
                .AnyAsync(r => r.CustomerId == customer.Id && r.CategoryId == category.Id, cancellationToken))
                throw new InvalidOperationException("This customer already has that document requirement.");

            var now = DateTime.UtcNow;
            var requirement = CustomerDocumentAssignment.FromCategory(customer, category, actor, now);
            requirement.IsRequired = required;
            if (dueDate.HasValue)
                requirement.DueDate = dueDate;
            _context.CustomerDocumentRequirements.Add(requirement);
            RecordAudit(CustomerDocumentAction.CategoryAssigned, actor, customer, requirement, category,
                newStatus: CustomerDocumentStatus.Missing, notes: "Category assigned to this customer.");
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new InvalidOperationException("This customer already has that document requirement.", ex);
            }
            return await LoadRequirementAsync(customer.Id, requirement.Id, cancellationToken);
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
                    newStatus: CustomerDocumentStatus.Missing, notes: "Assigned by a confirmed bulk action.");
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
                .Include(r => r.Versions)
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
            var versions = requirement.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new CustomerDocumentVersionDto
                {
                    Id = v.Id,
                    VersionNumber = v.VersionNumber,
                    IsCurrent = v.IsCurrent,
                    OriginalFileName = v.OriginalFileName,
                    ContentType = v.ContentType,
                    FileSize = v.FileSize,
                    UploadedByName = v.UploadedByName,
                    UploadedAt = v.UploadedAt,
                    ReviewStatus = v.ReviewStatus,
                    ReviewedByName = v.ReviewedByName,
                    ReviewedAt = v.ReviewedAt,
                    ReviewReason = v.ReviewReason
                }).ToList();

            return new CustomerDocumentRequirementDto
            {
                Id = requirement.Id,
                CategoryId = requirement.CategoryId,
                CategoryCode = requirement.Category?.Code,
                CategoryIsActive = requirement.Category?.IsActive ?? false,
                Name = requirement.Name,
                Description = requirement.Description,
                IsRequired = requirement.IsRequired,
                DisplayOrder = requirement.DisplayOrder,
                AllowedFileTypes = SplitTypes(requirement.AllowedFileTypes),
                MaxFileSizeBytes = requirement.MaxFileSizeBytes,
                Status = requirement.Status,
                DueDate = requirement.DueDate,
                PostponedUntil = requirement.PostponedUntil,
                LastActionByName = requirement.LastActionByName,
                UpdatedAt = requirement.UpdatedAt,
                ConcurrencyToken = Convert.ToBase64String(requirement.RowVersion),
                LatestVersion = versions.SingleOrDefault(v => v.IsCurrent),
                Versions = versions
            };
        }

        private static void EnsureUploadAllowed(CustomerDocumentStatus status)
        {
            if (status is not (CustomerDocumentStatus.Missing
                or CustomerDocumentStatus.Requested
                or CustomerDocumentStatus.Rejected
                or CustomerDocumentStatus.ReplacementRequired
                or CustomerDocumentStatus.Postponed
                or CustomerDocumentStatus.Expired
                or CustomerDocumentStatus.Approved))
                throw new InvalidOperationException($"A document cannot be uploaded while the requirement is {status}.");
        }

        private static void EnsureOverrideAllowed(CustomerDocumentStatus current)
        {
            if (current is CustomerDocumentStatus.Approved or CustomerDocumentStatus.Waived or CustomerDocumentStatus.NotApplicable)
                throw new InvalidOperationException($"A {current} requirement cannot be overridden without first creating a new requirement.");
        }

        private static void EnsureCurrentIs(CustomerDocumentStatus current, params CustomerDocumentStatus[] allowed)
        {
            if (!allowed.Contains(current))
                throw new InvalidOperationException($"The requested status change is not allowed from {current}.");
        }

        private static CustomerDocumentVersion RequireCurrentVersion(CustomerDocumentVersion? version) =>
            version ?? throw new InvalidOperationException("The requirement has no current uploaded version to review.");

        private static void ApplyReview(
            CustomerDocumentVersion version,
            CustomerDocumentVersionStatus status,
            CustomerDocumentActor actor,
            DateTime now,
            string? reason)
        {
            version.ReviewStatus = status;
            version.ReviewedByUserId = actor.UserId;
            version.ReviewedByName = actor.DisplayName;
            version.ReviewedAt = now;
            version.ReviewReason = reason;
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
            _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

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
                Notes = CleanOptional(notes, 2000),
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

        private static (string Name, string Code, string? Description, string AllowedTypes) ValidateCategory(
            string name,
            string code,
            string? description,
            IEnumerable<string> allowedTypes,
            long maxFileSize,
            int? defaultDueDays)
        {
            var normalizedCode = CleanRequired(code, 80, "Category code").ToLowerInvariant();
            if (!Regex.IsMatch(normalizedCode, "^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Category code may contain lowercase letters, numbers, underscores, and hyphens only.");
            ValidateMaxFileSize(maxFileSize);
            if (defaultDueDays is <= 0 or > 3650)
                throw new InvalidOperationException("Default due period must be between 1 and 3650 days.");
            return (CleanRequired(name, 150, "Category name"), normalizedCode,
                CleanOptional(description, 1000), NormalizeAllowedTypes(allowedTypes));
        }

        private static void ValidateMaxFileSize(long maxFileSize)
        {
            if (maxFileSize is < 1 or > MaxFileSize)
                throw new InvalidOperationException("Maximum file size must be between 1 byte and 25 MB.");
        }

        private static string NormalizeAllowedTypes(IEnumerable<string>? values)
        {
            var types = (values ?? []).Select(v => v.Trim().ToLowerInvariant())
                .Select(v => v.StartsWith('.') ? v : "." + v)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (types.Count == 0 || types.Any(t => !SupportedTypes.Contains(t)))
                throw new InvalidOperationException("Allowed file types must be one or more of PDF, JPG, JPEG, and PNG.");
            return string.Join(',', types);
        }

        private static List<string> SplitTypes(string types) =>
            types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static void ValidateAssignment(CustomerDocumentAssignmentMode mode, List<int>? selected)
        {
            if (!Enum.IsDefined(mode))
                throw new InvalidOperationException("The category assignment choice is invalid.");
            if (mode == CustomerDocumentAssignmentMode.SelectedCustomers && (selected == null || selected.Count == 0))
                throw new InvalidOperationException("Choose at least one customer for selected-customer assignment.");
            if ((selected?.Distinct().Count() ?? 0) > 5000)
                throw new InvalidOperationException("Select at most 5000 customers in one action.");
        }

        private static string RequireReason(string? value, string message) =>
            string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value;

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

        private static string FormatDate(DateTime? date) => date?.ToString("yyyy-MM-dd") ?? "not set";

        private static bool IsUniqueViolation(DbUpdateException exception) =>
            exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
