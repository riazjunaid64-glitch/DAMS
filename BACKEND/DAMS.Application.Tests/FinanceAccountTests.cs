using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class FinanceAccountTests
{
    [Fact]
    public async Task Balance_IsOpeningPlusRevenueMinusExpenses()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        context.ManualRevenues.Add(new ManualRevenue { FinanceAccountId = 1, Amount = 500, RevenueType = "Income" });
        context.Expenses.Add(new Expense { FinanceAccountId = 1, Amount = 200, Category = "Office" });
        await context.SaveChangesAsync();

        var result = await new FinanceAccountService(context).GetByIdAsync(1);
        Assert.Equal(1300, result.CurrentBalance);
        Assert.Equal(300, result.NetMovement);
        Assert.Equal(2, result.TransactionCount);
    }

    [Fact]
    public async Task InactiveAccount_CannotBeNewlyAssigned_ButCanRemainOnExistingRecord()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account(isActive: false));
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.EnsureSelectableAsync(1));
        await accounts.EnsureSelectableAsync(1, currentAccountId: 1);
    }

    [Fact]
    public async Task NewTransactionsRequireAnAccount_AndLegacyUnassignedFilteringStillWorks()
    {
        await using var context = Context();
        context.ManualRevenues.Add(new ManualRevenue { Amount = 10, RevenueType = "Legacy" });
        await context.SaveChangesAsync();
        var finance = new FinanceService(context, new NoopStorage(), new FinanceAccountService(context), NullLogger<FinanceService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => finance.CreateExpenseAsync(new CreateExpenseDto { Amount = 1, Category = "Office" }, 1));
        var rows = await finance.GetRevenuePageAsync(null, null, null, 0, 20, unassigned: true);
        Assert.Contains(rows.Items, x => x.RevenueType == "Legacy" && x.FinanceAccountId == null);
    }

    [Fact]
    public async Task AccountFilteredSummary_DoesNotIncludeUnassignedBookingReceivables()
    {
        await using var context = Context();
        context.FinanceAccounts.Add(Account());
        await context.SaveChangesAsync();
        var finance = new FinanceService(context, new NoopStorage(), new FinanceAccountService(context), NullLogger<FinanceService>.Instance);

        var summary = await finance.GetSummaryAsync(null, null, null, accountId: 1);

        Assert.Equal(0, summary.OutstandingAmount);
        Assert.Equal(0, summary.OverdueAmount);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static FinanceAccount Account(bool isActive = true) => new() { Id = 1, Name = "Main Cash", Type = FinanceAccountType.Cash, AccountHolderName = "Admin", OpeningBalance = 1000, IsActive = isActive };

    private sealed class NoopStorage : DAMS.Application.Interfaces.IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
