using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.DTOs.CustomerDocumentDtos;
using DAMS.Application.DTOs.CustomerDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>KAN-83: one switch per document, create, rename, remove, and who may call it.</summary>
public sealed class DocumentSetupKan83Tests
{
    private static readonly CustomerDocumentActor Admin = new(10, "Ayesha Admin");

    [Fact]
    public async Task Setup_ListsSeededDocuments_OmitsOther_AndCountsCustomersWhoAreNotBlocked()
    {
        await using var db = Context();
        db.Customers.Add(new Customer { FullName = "Open", Phone = "03001110001", Status = CustomerStatus.Active });
        db.Customers.Add(new Customer { FullName = "Closed", Phone = "03001110002", Status = CustomerStatus.Blocked });
        await db.SaveChangesAsync();

        var setup = await Service(db).GetSetupAsync();

        Assert.Equal(
            ["CNIC Front", "CNIC Back", "Customer Photograph", "Proof of Address", "Passport", "Next-of-Kin CNIC", "Signature Specimen", "Tax Document"],
            setup.Documents.Select(d => d.Name));
        Assert.DoesNotContain(setup.Documents, d => d.Name == "Other");
        Assert.True(setup.Documents.Single(d => d.Name == "CNIC Front").AsksEveryCustomer);
        Assert.False(setup.Documents.Single(d => d.Name == "Passport").AsksEveryCustomer);
        Assert.Equal(1, setup.NonBlockedCustomerCount);
    }

    [Fact]
    public async Task SwitchOn_AsksEveryNonBlockedCustomer_LeavesUploadedAndNotNeeded_AndRevivesAQuietCopy()
    {
        await using var db = Context();
        var service = Service(db);
        var open = await Customer(db, "Open Customer", CustomerStatus.Active);
        var blocked = await Customer(db, "Blocked Customer", CustomerStatus.Blocked);
        var uploaded = await Customer(db, "Already Uploaded", CustomerStatus.Active);
        var setAside = await Customer(db, "Not Needed", CustomerStatus.Active);
        var quiet = await Customer(db, "Quiet Copy", CustomerStatus.Active);
        var passport = await db.CustomerDocumentCategories.SingleAsync(c => c.Name == "Passport");

        var uploadedRow = Row(uploaded.Id, passport, CustomerDocumentStatus.Uploaded);
        db.CustomerDocumentRequirements.Add(uploadedRow);
        db.CustomerDocumentVersions.Add(new CustomerDocumentVersion
        {
            Requirement = uploadedRow, VersionNumber = 1, IsCurrent = true, StoredFileName = "p.pdf",
            OriginalFileName = "p.pdf", ContentType = "application/pdf", FileSize = 10, UploadedAt = DateTime.UtcNow
        });
        db.CustomerDocumentRequirements.Add(Row(setAside.Id, passport, CustomerDocumentStatus.NotNeeded));
        db.CustomerDocumentRequirements.Add(new CustomerDocumentRequirement
        {
            CustomerId = quiet.Id, CategoryId = passport.Id, Name = "Passport",
            IsRequired = false, IsSuppressed = true, Status = CustomerDocumentStatus.Needed
        });
        await db.SaveChangesAsync();

        var result = await service.SetAsksEveryCustomerAsync(passport.Id, true, Admin);

        Assert.True(result.AsksEveryCustomer);
        Assert.Equal(CustomerDocumentStatus.Needed, await Status(db, open.Id, passport.Id));
        Assert.False(await db.CustomerDocumentRequirements.AnyAsync(r => r.CustomerId == blocked.Id && r.CategoryId == passport.Id));
        Assert.Equal(CustomerDocumentStatus.Uploaded, await Status(db, uploaded.Id, passport.Id));
        Assert.Equal(CustomerDocumentStatus.NotNeeded, await Status(db, setAside.Id, passport.Id));
        var revived = await db.CustomerDocumentRequirements.SingleAsync(r => r.CustomerId == quiet.Id && r.CategoryId == passport.Id);
        Assert.Equal(CustomerDocumentStatus.Needed, revived.Status);
        Assert.False(revived.IsSuppressed);
        Assert.True(revived.IsRequired);
        Assert.Equal(1, await db.CustomerDocumentRequirements.CountAsync(r => r.CustomerId == open.Id && r.CategoryId == passport.Id));

        var customerAudits = await db.CustomerDocumentAuditEntries.CountAsync(a =>
            a.Action == CustomerDocumentAction.CategoryAssigned && a.CategoryId == passport.Id);
        var switchAudit = await db.CustomerDocumentAuditEntries.CountAsync(a =>
            a.Action == CustomerDocumentAction.BulkCategoryAssignment && a.CategoryId == passport.Id && a.CustomerId == null);
        Assert.True(customerAudits >= 1);
        Assert.Equal(1, switchAudit);

        var future = await new CustomerService(db).CreateCustomerAsync(new CreateCustomerDto
        {
            FullName = "Future Customer", Phone = "03008880000"
        }, Admin.UserId, Admin.DisplayName);
        Assert.True(await db.CustomerDocumentRequirements.AnyAsync(r =>
            r.CustomerId == future.Id && r.CategoryId == passport.Id && r.Status == CustomerDocumentStatus.Needed && r.IsRequired));
    }

    [Fact]
    public async Task SwitchOff_DropsCopiesWithoutAFile_AndKeepsCopiesThatHaveOne()
    {
        await using var db = Context();
        var service = Service(db);
        var bare = await Customer(db, "No File", CustomerStatus.Active);
        var filed = await Customer(db, "Has File", CustomerStatus.Active);
        var passport = await db.CustomerDocumentCategories.SingleAsync(c => c.Name == "Passport");
        await service.SetAsksEveryCustomerAsync(passport.Id, true, Admin);

        var filedRow = await db.CustomerDocumentRequirements.SingleAsync(r => r.CustomerId == filed.Id && r.CategoryId == passport.Id);
        db.CustomerDocumentVersions.Add(new CustomerDocumentVersion
        {
            RequirementId = filedRow.Id, VersionNumber = 1, IsCurrent = true, StoredFileName = "p.pdf",
            OriginalFileName = "p.pdf", ContentType = "application/pdf", FileSize = 10, UploadedAt = DateTime.UtcNow
        });
        filedRow.Status = CustomerDocumentStatus.Uploaded;
        await db.SaveChangesAsync();

        await service.SetAsksEveryCustomerAsync(passport.Id, false, Admin);

        var bareRow = await db.CustomerDocumentRequirements.SingleAsync(r => r.CustomerId == bare.Id && r.CategoryId == passport.Id);
        Assert.True(bareRow.IsSuppressed);
        Assert.False(bareRow.IsRequired);
        var checklist = await service.GetChecklistAsync(bare.Id);
        Assert.DoesNotContain(checklist.Requirements, r => r.CategoryId == passport.Id);

        var kept = await db.CustomerDocumentRequirements.SingleAsync(r => r.CustomerId == filed.Id && r.CategoryId == passport.Id);
        Assert.False(kept.IsSuppressed);
        Assert.Equal(CustomerDocumentStatus.Uploaded, kept.Status);
        Assert.Contains((await service.GetChecklistAsync(filed.Id)).Requirements, r => r.Id == kept.Id && r.Status == CustomerDocumentStatus.Uploaded);
        Assert.False((await db.CustomerDocumentCategories.SingleAsync(c => c.Id == passport.Id)).AsksEveryCustomer);
    }

    [Fact]
    public async Task CreateRenameAndRemove_EnforceAUniqueName_AndHideAUsedDocument()
    {
        await using var db = Context();
        var service = Service(db);

        var created = await service.CreateDocumentAsync(new SaveDocumentNameDto { Name = "  Visa copy  " }, Admin);
        Assert.Equal("Visa copy", created.Name);
        Assert.False(created.AsksEveryCustomer);
        Assert.Equal(created.Id, (await service.GetSetupAsync()).Documents[^1].Id);

        var clash = await Assert.ThrowsAsync<CustomerDocumentConflictException>(() =>
            service.CreateDocumentAsync(new SaveDocumentNameDto { Name = "visa COPY" }, Admin));
        Assert.Equal(CustomerDocumentService.NameClashMessage, clash.Message);

        var customer = await Customer(db, "Holder", CustomerStatus.Active);
        await service.RenameDocumentAsync(created.Id, new SaveDocumentNameDto { Name = "Visa" }, Admin);
        db.CustomerDocumentRequirements.Add(new CustomerDocumentRequirement
        {
            CustomerId = customer.Id, CategoryId = created.Id, Name = "Visa", Status = CustomerDocumentStatus.Needed, IsRequired = true
        });
        await db.SaveChangesAsync();
        await service.RenameDocumentAsync(created.Id, new SaveDocumentNameDto { Name = "Residence visa" }, Admin);
        Assert.Equal("Residence visa", (await db.CustomerDocumentRequirements.SingleAsync(r => r.CategoryId == created.Id)).Name);

        var clashRename = await Assert.ThrowsAsync<CustomerDocumentConflictException>(() =>
            service.RenameDocumentAsync(created.Id, new SaveDocumentNameDto { Name = "passport" }, Admin));
        Assert.Equal(CustomerDocumentService.NameClashMessage, clashRename.Message);

        await service.RemoveDocumentAsync(created.Id, Admin);
        var hidden = await db.CustomerDocumentCategories.SingleAsync(c => c.Id == created.Id);
        Assert.True(hidden.IsHidden);
        Assert.False(hidden.AsksEveryCustomer);
        Assert.DoesNotContain((await service.GetSetupAsync()).Documents, d => d.Id == created.Id);
        Assert.DoesNotContain((await service.GetChecklistAsync(customer.Id)).AvailableTypes, t => t.CategoryId == created.Id);
        Assert.True((await db.CustomerDocumentRequirements.SingleAsync(r => r.CategoryId == created.Id)).IsSuppressed);

        var unused = await service.CreateDocumentAsync(new SaveDocumentNameDto { Name = "Unused slip" }, Admin);
        await service.RemoveDocumentAsync(unused.Id, Admin);
        Assert.False(await db.CustomerDocumentCategories.AnyAsync(c => c.Id == unused.Id));
    }

    [Fact]
    public async Task Reconciliation_AddsOnDocuments_OnlyForCustomersWhoAreNotBlocked_AndLackTheCopy()
    {
        await using var db = Context();
        var missing = await Customer(db, "Missing", CustomerStatus.Active);
        var blocked = await Customer(db, "Blocked", CustomerStatus.Blocked);
        var reconciler = new CustomerDocumentReconciliationService(db, NullLogger<CustomerDocumentReconciliationService>.Instance);

        Assert.Equal(1, await reconciler.ReconcileBatchAsync());
        Assert.Equal(3, await db.CustomerDocumentRequirements.CountAsync(r => r.CustomerId == missing.Id));
        Assert.False(await db.CustomerDocumentRequirements.AnyAsync(r => r.CustomerId == blocked.Id));
        Assert.Equal(0, await reconciler.ReconcileBatchAsync());
    }

    [Fact]
    public void CustomersCapability_IsAdminAndAccountantOnly()
    {
        Assert.True(AppCapabilities.Can("Admin", "customers"));
        Assert.True(AppCapabilities.Can("Accountant", "customers"));
        Assert.False(AppCapabilities.Can("Manager", "customers"));
        Assert.False(AppCapabilities.Can("Employee", "customers"));
        Assert.False(AppCapabilities.Can("Client", "customers"));
        Assert.False(AppCapabilities.Can(null, "customers"));
    }

    private static async Task<CustomerDocumentStatus> Status(AppDbContext db, int customerId, int categoryId) =>
        await db.CustomerDocumentRequirements.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.CategoryId == categoryId)
            .Select(r => r.Status)
            .SingleAsync();

    private static CustomerDocumentRequirement Row(int customerId, CustomerDocumentCategory category, CustomerDocumentStatus status) => new()
    {
        CustomerId = customerId,
        CategoryId = category.Id,
        Name = category.Name,
        IsRequired = true,
        Status = status
    };

    private static async Task<Customer> Customer(AppDbContext db, string name, CustomerStatus status)
    {
        var customer = new Customer { FullName = name, Phone = Guid.NewGuid().ToString("N"), Status = status };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static CustomerDocumentService Service(AppDbContext db) =>
        new(db, new MemoryStorage(), NullLogger<CustomerDocumentService>.Instance);

    private static AppDbContext Context()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class MemoryStorage : ICustomerDocumentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("stored" + extension);
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream());
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
