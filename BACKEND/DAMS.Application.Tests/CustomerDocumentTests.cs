using System.Reflection;
using System.Text;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class CustomerDocumentTests
{
    private static readonly CustomerDocumentActor Admin = new(10, "Ayesha Admin");

    [Fact]
    public async Task NewCustomer_ReceivesOnlyActiveFutureCategories_WithoutBlockingCreation()
    {
        await using var db = Context();
        var inactive = await db.CustomerDocumentCategories.SingleAsync(c => c.Code == "passport");
        inactive.IsActive = false;
        await db.SaveChangesAsync();

        var customer = await new CustomerService(db).CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Ali Khan",
            Phone = "0300-1234567"
        }, Admin.UserId, Admin.DisplayName);

        var requirements = await db.CustomerDocumentRequirements.Where(r => r.CustomerId == customer.Id).ToListAsync();
        Assert.Equal(8, requirements.Count);
        Assert.DoesNotContain(requirements, r => r.CategoryId == inactive.Id);
        Assert.All(requirements, r => Assert.Equal(CustomerDocumentStatus.Missing, r.Status));
        Assert.Equal(3, customer.DocumentSummary.RequiredTotal);
        Assert.Equal(3, customer.DocumentSummary.Missing);
    }

    [Fact]
    public async Task Category_CreateEditDuplicateCodeAndUsedDelete_AreControlled()
    {
        await using var db = Context();
        var service = Service(db);
        var category = await service.CreateCategoryAsync(Category("proof_of_income"), Admin);
        Assert.Equal("proof_of_income", category.Code);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCategoryAsync(Category("proof_of_income"), Admin));

        var customer = await Customer(db, "Customer One");
        await service.AssignCategoryAsync(category.Id, new AssignCustomerDocumentCategoryDto
        {
            AssignmentMode = CustomerDocumentAssignmentMode.SelectedCustomers,
            SelectedCustomerIds = [customer.Id]
        }, Admin);

        category = (await service.GetCategoriesAsync(true)).Single(c => c.Id == category.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCategoryAsync(category.Id,
            new UpdateCustomerDocumentCategoryDto
            {
                Name = category.Name,
                Code = "changed_code",
                IsRequiredByDefault = true,
                DisplayOrder = 100,
                AllowedFileTypes = [".pdf"],
                MaxFileSizeBytes = 1024 * 1024,
                IsActive = false,
                ConcurrencyToken = category.ConcurrencyToken
            }, Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCategoryAsync(category.Id, Admin));
    }

    [Fact]
    public async Task BulkAssignment_IsIdempotent_AndAudited()
    {
        await using var db = Context();
        var service = Service(db);
        var first = await Customer(db, "First");
        var second = await Customer(db, "Second");
        var category = await service.CreateCategoryAsync(Category("updated_cnic"), Admin);
        var request = new AssignCustomerDocumentCategoryDto
        {
            AssignmentMode = CustomerDocumentAssignmentMode.SelectedCustomers,
            SelectedCustomerIds = [first.Id, second.Id]
        };

        var initial = await service.AssignCategoryAsync(category.Id, request, Admin);
        var repeated = await service.AssignCategoryAsync(category.Id, request, Admin);

        Assert.Equal(2, initial.AssignedCustomers);
        Assert.Equal(0, repeated.AssignedCustomers);
        Assert.Equal(2, repeated.AlreadyAssignedCustomers);
        Assert.Equal(2, await db.CustomerDocumentRequirements.CountAsync(r => r.CategoryId == category.Id));
        Assert.True(await db.CustomerDocumentAuditEntries.AnyAsync(a => a.Action == CustomerDocumentAction.BulkCategoryAssignment));
    }

    [Fact]
    public async Task AllActiveAssignment_DoesNotSilentlyApplyToFutureCustomers()
    {
        await using var db = Context();
        var service = Service(db);
        var existing = await Customer(db, "Existing Customer");
        var category = await service.CreateCategoryAsync(new CreateCustomerDocumentCategoryDto
        {
            Name = "Existing Population Check",
            Code = "existing_population_check",
            IsRequiredByDefault = true,
            AllowedFileTypes = [".pdf"],
            MaxFileSizeBytes = 1024 * 1024,
            AssignmentMode = CustomerDocumentAssignmentMode.AllActiveCustomers
        }, Admin);

        Assert.True(await db.CustomerDocumentRequirements.AnyAsync(r => r.CustomerId == existing.Id && r.CategoryId == category.Id));
        Assert.False(category.AssignToNewCustomers);

        var future = await new CustomerService(db).CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Future Customer",
            Phone = "03009999999"
        }, Admin.UserId, Admin.DisplayName);
        Assert.False(await db.CustomerDocumentRequirements.AnyAsync(r => r.CustomerId == future.Id && r.CategoryId == category.Id));
    }

    [Fact]
    public async Task CustomRequirement_CanStayLocal_OrBecomeGlobal()
    {
        await using var db = Context();
        var service = Service(db);
        var customer = await Customer(db, "Overseas Customer");

        var local = await service.AddRequirementAsync(customer.Id, new AddCustomerDocumentRequirementDto
        {
            Name = "Overseas Income Declaration",
            IsRequired = true,
            AllowedFileTypes = ["pdf"]
        }, Admin);
        Assert.Null(local.CategoryId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddRequirementAsync(customer.Id,
            new AddCustomerDocumentRequirementDto
            {
                Name = "Overseas Income Declaration",
                IsRequired = true,
                AllowedFileTypes = [".pdf"]
            }, Admin));

        var global = await service.AddRequirementAsync(customer.Id, new AddCustomerDocumentRequirementDto
        {
            Name = "Residency Evidence",
            GlobalCategoryCode = "residency_evidence",
            SaveAsGlobalCategory = true,
            IsRequired = true,
            AllowedFileTypes = [".pdf", ".png"],
            GlobalAssignmentMode = CustomerDocumentAssignmentMode.None
        }, Admin);
        Assert.NotNull(global.CategoryId);
        Assert.True(await db.CustomerDocumentCategories.AnyAsync(c => c.Code == "residency_evidence"));
    }

    [Fact]
    public async Task UploadReviewAndReplacement_PreserveEveryVersion_AndOneCurrent()
    {
        await using var db = Context();
        var storage = new MemoryStorage();
        var service = Service(db, storage);
        var customer = await Customer(db, "Versioned Customer");
        var requirement = await service.AddRequirementAsync(customer.Id, new AddCustomerDocumentRequirementDto
        {
            Name = "Identity Scan", IsRequired = true, AllowedFileTypes = [".pdf"]
        }, Admin);

        requirement = await service.UploadAsync(customer.Id, requirement.Id, requirement.ConcurrencyToken,
            Pdf("../identity.pdf"), Admin);
        Assert.Equal(CustomerDocumentStatus.UnderReview, requirement.Status);
        Assert.Equal("identity.pdf", requirement.LatestVersion!.OriginalFileName);

        requirement = await service.ChangeStatusAsync(customer.Id, requirement.Id, new CustomerDocumentStatusChangeDto
        {
            Status = CustomerDocumentStatus.Rejected,
            Reason = "Image is unclear",
            ConcurrencyToken = requirement.ConcurrencyToken
        }, Admin);
        requirement = await service.ChangeStatusAsync(customer.Id, requirement.Id, new CustomerDocumentStatusChangeDto
        {
            Status = CustomerDocumentStatus.ReplacementRequired,
            Reason = "Upload a clearer scan",
            ConcurrencyToken = requirement.ConcurrencyToken
        }, Admin);
        requirement = await service.UploadAsync(customer.Id, requirement.Id, requirement.ConcurrencyToken,
            Pdf("identity-v2.pdf"), Admin);
        requirement = await service.ChangeStatusAsync(customer.Id, requirement.Id, new CustomerDocumentStatusChangeDto
        {
            Status = CustomerDocumentStatus.Approved,
            ConcurrencyToken = requirement.ConcurrencyToken
        }, Admin);

        Assert.Equal(CustomerDocumentStatus.Approved, requirement.Status);
        Assert.Equal(2, requirement.Versions.Count);
        Assert.Single(requirement.Versions, v => v.IsCurrent);
        Assert.Contains(requirement.Versions, v => v.VersionNumber == 1
                                                   && v.ReviewStatus == CustomerDocumentVersionStatus.Rejected
                                                   && v.ReviewReason == "Image is unclear");
        Assert.Contains(requirement.Versions, v => v.VersionNumber == 2
                                                   && v.ReviewStatus == CustomerDocumentVersionStatus.Approved);
        Assert.Equal(2, storage.Files.Count);
    }

    [Fact]
    public async Task Overrides_RequireReasons_AndUseCanonicalCompletionRules()
    {
        await using var db = Context();
        var service = Service(db);
        var customer = await Customer(db, "Override Customer");
        var waived = await service.AddRequirementAsync(customer.Id, Custom("Waived Item"), Admin);
        var notApplicable = await service.AddRequirementAsync(customer.Id, Custom("N/A Item"), Admin);
        var postponed = await service.AddRequirementAsync(customer.Id, Custom("Later Item"), Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeStatusAsync(customer.Id, waived.Id,
            new CustomerDocumentStatusChangeDto { Status = CustomerDocumentStatus.Waived, ConcurrencyToken = waived.ConcurrencyToken }, Admin));

        waived = await Change(service, customer.Id, waived, CustomerDocumentStatus.Waived, "Approved exception");
        notApplicable = await Change(service, customer.Id, notApplicable, CustomerDocumentStatus.NotApplicable, "Customer is not overseas");
        postponed = await service.ChangeStatusAsync(customer.Id, postponed.Id, new CustomerDocumentStatusChangeDto
        {
            Status = CustomerDocumentStatus.Postponed,
            Reason = "Bring at next visit",
            PostponedUntil = DateTime.UtcNow.AddDays(7),
            ConcurrencyToken = postponed.ConcurrencyToken
        }, Admin);

        var checklist = await service.GetChecklistAsync(customer.Id);
        Assert.Equal(3, checklist.Summary.RequiredTotal);
        Assert.Equal(2, checklist.Summary.CompletedRequired);
        Assert.Equal(1, checklist.Summary.Postponed);
        Assert.False(checklist.Summary.IsComplete);
        Assert.Contains(checklist.History, a => a.Action == CustomerDocumentAction.Waived);
        Assert.Contains(checklist.History, a => a.Action == CustomerDocumentAction.MarkedNotApplicable);
    }

    [Fact]
    public void Completion_PostponedCollectionDate_ReturnsRequirementToAttention()
    {
        var summary = CustomerDocumentCompletion.Calculate([
            new CustomerDocumentRequirement
            {
                IsRequired = true,
                Status = CustomerDocumentStatus.Postponed,
                PostponedUntil = DateTime.UtcNow.AddMinutes(-1)
            }
        ]);

        Assert.Equal(1, summary.Postponed);
        Assert.Equal(1, summary.PostponedDue);
        Assert.Equal("1 postponed due", summary.Label);
        Assert.False(summary.IsComplete);
    }

    [Fact]
    public async Task FileValidation_RejectsEmptyOversizeDisallowedAndMisleadingFiles()
    {
        await using var db = Context();
        var service = Service(db);
        var customer = await Customer(db, "Validation Customer");
        var requirement = await service.AddRequirementAsync(customer.Id, new AddCustomerDocumentRequirementDto
        {
            Name = "PDF only", IsRequired = true, AllowedFileTypes = [".pdf"], MaxFileSizeBytes = 1024
        }, Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Upload("empty.pdf", []), Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Upload("large.pdf", new byte[1025]), Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Upload("fake.pdf", Encoding.ASCII.GetBytes("not a pdf")), Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Png("photo.png"), Admin));
    }

    [Fact]
    public async Task ValidPngUpload_Succeeds_WhenCategoryAllowsImages()
    {
        await using var db = Context();
        var service = Service(db);
        var customer = await Customer(db, "Image Customer");
        var requirement = await service.AddRequirementAsync(customer.Id, new AddCustomerDocumentRequirementDto
        {
            Name = "Photograph", IsRequired = true, AllowedFileTypes = [".png"]
        }, Admin);

        requirement = await service.UploadAsync(customer.Id, requirement.Id, requirement.ConcurrencyToken,
            Png("photo.png"), Admin);
        Assert.Equal("image/png", requirement.LatestVersion!.ContentType);
        Assert.Equal(CustomerDocumentStatus.UnderReview, requirement.Status);
    }

    [Fact]
    public async Task StorageOrDatabaseFailure_DoesNotLeaveOrphanMetadataOrBytes()
    {
        await using var db = Context();
        var storage = new MemoryStorage { FailSaves = true };
        var service = Service(db, storage);
        var customer = await Customer(db, "Failure Customer");
        var requirement = await service.AddRequirementAsync(customer.Id, Custom("Failure Evidence"), Admin);

        await Assert.ThrowsAsync<IOException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Pdf("failure.pdf"), Admin));
        Assert.False(await db.CustomerDocumentVersions.AnyAsync());
        Assert.Empty(storage.Files);

        storage.FailSaves = false;
        db.Database.EnsureDeleted();
        db.ChangeTracker.Clear();
        db.Database.EnsureCreated();
        customer = await Customer(db, "Database Failure Customer");
        requirement = await service.AddRequirementAsync(customer.Id, Custom("Database Failure Evidence"), Admin);
        db.SavingChanges += (_, _) => throw new DbUpdateException("Simulated database write failure.");

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UploadAsync(customer.Id, requirement.Id,
            requirement.ConcurrencyToken, Pdf("database-failure.pdf"), Admin));
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task CrossCustomerAndGuessedVersionIds_DoNotReturnPrivateFiles()
    {
        await using var db = Context();
        var service = Service(db);
        var owner = await Customer(db, "Owner");
        var other = await Customer(db, "Other");
        var requirement = await service.AddRequirementAsync(owner.Id, Custom("Private Identity"), Admin);
        requirement = await service.UploadAsync(owner.Id, requirement.Id, requirement.ConcurrencyToken, Pdf("private.pdf"), Admin);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DownloadAsync(other.Id, requirement.Id,
            requirement.LatestVersion!.Id, Admin));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DownloadAsync(owner.Id, requirement.Id,
            requirement.LatestVersion!.Id + 9999, Admin));
        var download = await service.DownloadAsync(owner.Id, requirement.Id, requirement.LatestVersion!.Id, Admin);
        Assert.Equal("private.pdf", download.FileName);
        await download.Content.DisposeAsync();
    }

    [Fact]
    public void DatabaseModel_EnforcesUniqueRequirementAndCurrentVersion()
    {
        using var db = Context();
        var requirement = db.Model.FindEntityType(typeof(CustomerDocumentRequirement))!;
        Assert.Contains(requirement.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CustomerId", "CategoryId" }));
        var version = db.Model.FindEntityType(typeof(CustomerDocumentVersion))!;
        Assert.Contains(version.GetIndexes(), i => i.IsUnique && i.GetFilter() == "[IsCurrent] = 1");
    }

    [Fact]
    public void EveryCustomerDocumentEndpoint_IsServerRestrictedToAdmin()
    {
        var authorize = typeof(CustomerDocumentsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
    }

    private static AppDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CustomerDocumentService Service(AppDbContext db, MemoryStorage? storage = null) =>
        new(db, storage ?? new MemoryStorage(), NullLogger<CustomerDocumentService>.Instance);

    private static CreateCustomerDocumentCategoryDto Category(string code) => new()
    {
        Name = code.Replace('_', ' '),
        Code = code,
        IsRequiredByDefault = true,
        AllowedFileTypes = [".pdf"],
        MaxFileSizeBytes = 1024 * 1024,
        AssignmentMode = CustomerDocumentAssignmentMode.None
    };

    private static AddCustomerDocumentRequirementDto Custom(string name) => new()
    {
        Name = name,
        IsRequired = true,
        AllowedFileTypes = [".pdf"],
        MaxFileSizeBytes = 1024 * 1024
    };

    private static async Task<Customer> Customer(AppDbContext db, string name)
    {
        var customer = new Customer { FullName = name, Phone = Guid.NewGuid().ToString("N"), Status = CustomerStatus.Active };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static CustomerDocumentUpload Pdf(string name) =>
        Upload(name, Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"));

    private static CustomerDocumentUpload Png(string name) => Upload(name,
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

    private static CustomerDocumentUpload Upload(string name, byte[] bytes) => new()
    {
        FileName = name,
        Length = bytes.LongLength,
        Content = new MemoryStream(bytes)
    };

    private static Task<CustomerDocumentRequirementDto> Change(
        CustomerDocumentService service,
        int customerId,
        CustomerDocumentRequirementDto requirement,
        CustomerDocumentStatus status,
        string reason) => service.ChangeStatusAsync(customerId, requirement.Id, new CustomerDocumentStatusChangeDto
    {
        Status = status,
        Reason = reason,
        ConcurrencyToken = requirement.ConcurrencyToken
    }, Admin);

    private sealed class MemoryStorage : ICustomerDocumentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public bool FailSaves { get; set; }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Simulated storage failure.");
            var key = $"{Guid.NewGuid():N}{extension}";
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Files[key] = memory.ToArray();
            return key;
        }

        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storedFileName, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            Files.Remove(storedFileName);
            return Task.CompletedTask;
        }
    }
}
