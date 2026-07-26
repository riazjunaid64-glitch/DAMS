using System.Reflection;
using DAMS.Api.Controllers;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceAttachmentTests
{
    [Fact]
    public async Task Revenue_FullAttachmentLifecycle_Works()
    {
        await using var context = CreateContext();
        var storage = new MemoryAttachmentStorage();
        var service = CreateService(context, storage);
        var created = await service.CreateManualRevenueAsync(
            new CreateManualRevenueDto { FinanceAccountId = 1, Amount = 1250, RevenueType = "Other Income" }, 1, Pdf("receipt.pdf"));

        Assert.NotNull(created.Attachment);
        Assert.Equal("receipt.pdf", created.Attachment.FileName);
        var download = await service.GetAttachmentAsync(FinanceRecordKind.Revenue, created.Id);
        await using (download.Content) Assert.True(download.Content.Length > 0);

        var updated = await service.UpdateManualRevenueAsync(
            created.Id,
            new UpdateManualRevenueDto { FinanceAccountId = 1, Amount = 1300, RevenueType = "Other Income" },
            Pdf("replacement.pdf"));
        Assert.Equal("replacement.pdf", updated.Attachment?.FileName);
        Assert.Single(storage.Files);

        await service.RemoveAttachmentAsync(FinanceRecordKind.Revenue, created.Id);
        Assert.Empty(storage.Files);
        Assert.True(await context.ManualRevenues.AnyAsync(r => r.Id == created.Id));
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetAttachmentAsync(FinanceRecordKind.Revenue, created.Id));
    }

    [Fact]
    public async Task Expense_FullAttachmentLifecycle_AndDeleteCleanup_Work()
    {
        await using var context = CreateContext();
        var storage = new MemoryAttachmentStorage();
        var service = CreateService(context, storage);
        var created = await service.CreateExpenseAsync(
            new CreateExpenseDto { FinanceAccountId = 1, Amount = 500, Category = "Office" }, 1, Pdf("bill.pdf"));

        Assert.NotNull(created.Attachment);
        Assert.Single(storage.Files);

        var removed = await service.UpdateExpenseAsync(
            created.Id,
            new UpdateExpenseDto { FinanceAccountId = 1, Amount = 500, Category = "Office" },
            removeAttachment: true);
        Assert.Null(removed.Attachment);
        Assert.Empty(storage.Files);
        Assert.True(await context.Expenses.AnyAsync(e => e.Id == created.Id));

        await service.UpdateExpenseAsync(
            created.Id,
            new UpdateExpenseDto { FinanceAccountId = 1, Amount = 500, Category = "Office" },
            Pdf("new-bill.pdf"));
        Assert.Single(storage.Files);
        await service.DeleteExpenseAsync(created.Id);
        Assert.Empty(storage.Files);
        Assert.False(await context.Expenses.AnyAsync(e => e.Id == created.Id));
    }

    [Fact]
    public async Task RecordsWithoutAttachments_AndPreFeatureRecords_StillWork()
    {
        await using var context = CreateContext();
        var service = CreateService(context, new MemoryAttachmentStorage());
        var revenue = await service.CreateManualRevenueAsync(
            new CreateManualRevenueDto { FinanceAccountId = 1, Amount = 100, RevenueType = "Other Income" }, 1);
        var expense = await service.CreateExpenseAsync(
            new CreateExpenseDto { FinanceAccountId = 1, Amount = 100, Category = "Office" }, 1);
        Assert.Null(revenue.Attachment);
        Assert.Null(expense.Attachment);

        context.ManualRevenues.Add(new ManualRevenue { Amount = 10, RevenueType = "Legacy" });
        context.Expenses.Add(new Expense { Amount = 10, Category = "Legacy" });
        await context.SaveChangesAsync();
        var revenueRows = await service.GetRevenuePageAsync(null, null, null, 0, 100);
        var expenseRows = await service.GetExpensePageAsync(null, null, null, 0, 100);
        Assert.Contains(revenueRows.Items, row => row.RevenueType == "Legacy" && row.Attachment == null);
        Assert.Contains(expenseRows.Items, row => row.Category == "Legacy" && row.Attachment == null);
    }

    [Fact]
    public async Task FailedUpload_DoesNotCreateRecord_OrReplaceExistingAttachment()
    {
        await using var context = CreateContext();
        var storage = new MemoryAttachmentStorage { FailSaves = true };
        var service = CreateService(context, storage);
        await Assert.ThrowsAsync<IOException>(() => service.CreateExpenseAsync(
            new CreateExpenseDto { FinanceAccountId = 1, Amount = 100, Category = "Office" }, 1, Pdf("bill.pdf")));
        Assert.False(await context.Expenses.AnyAsync());

        storage.FailSaves = false;
        var revenue = await service.CreateManualRevenueAsync(
            new CreateManualRevenueDto { FinanceAccountId = 1, Amount = 100, RevenueType = "Other Income" }, 1, Pdf("original.pdf"));
        storage.FailSaves = true;
        await Assert.ThrowsAsync<IOException>(() => service.UpdateManualRevenueAsync(
            revenue.Id, new UpdateManualRevenueDto { FinanceAccountId = 1, Amount = 999, RevenueType = "Changed" }, Pdf("replacement.pdf")));
        context.ChangeTracker.Clear();
        var unchanged = await context.ManualRevenues.Include(r => r.Attachment).SingleAsync();
        Assert.Equal(100, unchanged.Amount);
        Assert.Equal("original.pdf", unchanged.Attachment?.OriginalFileName);
    }

    [Fact]
    public async Task DatabaseFailureAfterUpload_CleansUpNewFile()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new FailingDbContext(options);
        context.FinanceAccounts.Add(TestAccount());
        await context.SaveChangesAsync();
        context.FailNextSave = true;
        var storage = new MemoryAttachmentStorage();
        var service = CreateService(context, storage);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateManualRevenueAsync(
            new CreateManualRevenueDto { FinanceAccountId = 1, Amount = 100, RevenueType = "Other Income" }, 1, Pdf("proof.pdf")));
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task MissingStoredFile_ReturnsAnUnderstandableNotFoundFailure()
    {
        await using var context = CreateContext();
        var storage = new MemoryAttachmentStorage();
        var service = CreateService(context, storage);
        var expense = await service.CreateExpenseAsync(
            new CreateExpenseDto { FinanceAccountId = 1, Amount = 100, Category = "Office" }, 1, Pdf("proof.pdf"));
        storage.Files.Clear();
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetAttachmentAsync(FinanceRecordKind.Expense, expense.Id));
        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("empty.pdf", "", "empty")]
    [InlineData("malware.exe", "MZ", "not supported")]
    [InlineData("fake.pdf", "not really a pdf", "not a PDF")]
    public void InvalidFiles_AreRejected(string name, string content, string message)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var upload = new FinanceAttachmentUpload { Content = new MemoryStream(bytes), FileName = name, Length = bytes.Length };
        var error = Assert.Throws<InvalidOperationException>(() => FinanceAttachmentFileValidator.Validate(upload));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedFiles_AreRejectedBeforeStorage()
    {
        var upload = new FinanceAttachmentUpload
        {
            Content = new MemoryStream(new byte[1]), FileName = "large.pdf",
            Length = FinanceAttachmentFileValidator.MaxFileSize + 1
        };
        var error = Assert.Throws<InvalidOperationException>(() => FinanceAttachmentFileValidator.Validate(upload));
        Assert.Contains("too large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinanceController_IsRestrictedToAdmins()
    {
        var authorize = typeof(FinanceController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public async Task PrivateStorage_RejectsPathTraversal_AndRoundTripsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dams-finance-tests-{Guid.NewGuid():N}");
        try
        {
            var storage = new PrivateFinanceAttachmentStorage(root);
            await using var source = new MemoryStream([1, 2, 3]);
            var key = await storage.SaveAsync(source, ".pdf");
            Assert.DoesNotContain("/", key);
            Assert.DoesNotContain("\\", key);
            await using var opened = await storage.OpenReadAsync(key);
            Assert.NotNull(opened);
            Assert.Equal(3, opened.Length);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync("../outside.pdf"));
            await storage.DeleteAsync(key);
            Assert.Null(await storage.OpenReadAsync(key));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AppDbContext CreateContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.FinanceAccounts.Add(TestAccount());
        context.SaveChanges();
        return context;
    }

    private static FinanceAccount TestAccount() => new() { Id = 1, Name = "Test Cash", AccountHolderName = "Test Holder", IsActive = true };

    private static FinanceService CreateService(AppDbContext context, IFinanceAttachmentStorage storage) =>
        new(context, storage, new FinanceAccountService(context), NullLogger<FinanceService>.Instance);

    private static FinanceAttachmentUpload Pdf(string name)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
        return new FinanceAttachmentUpload
        {
            Content = new MemoryStream(bytes), FileName = name, ContentType = "application/pdf", Length = bytes.Length
        };
    }

    private sealed class MemoryAttachmentStorage : IFinanceAttachmentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool FailSaves { get; set; }
        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Simulated storage failure");
            var key = $"{Guid.NewGuid():N}{extension}";
            content.Position = 0;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Files[key] = buffer.ToArray();
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

    private sealed class FailingDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!FailNextSave) return base.SaveChangesAsync(cancellationToken);
            FailNextSave = false;
            throw new DbUpdateException("Simulated database failure");
        }
    }
}
