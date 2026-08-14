using DAMS.Application.Common;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class StaffCashTests
{
    [Fact]
    public async Task GivingSpendingAndReturningCash_MovesTheBalanceWithoutPrematureExpense()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Name = "Main cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 100_000m, IsActive = true
        };
        var capital = new FinanceAccount
        {
            Name = "Owner capital", AccountHolderName = "Owners",
            Type = FinanceAccountType.Capital, OpeningBalance = 100_000m, IsActive = true
        };
        var lunch = Category(1, "Lunch");
        var office = Category(2, "Office supplies");
        context.AddRange(cash, capital, lunch, office);
        await context.SaveChangesAsync();

        var accounts = new FinanceAccountService(context);
        var staff = new StaffCashService(context, accounts);
        var finance = Finance(context, accounts);
        var holder = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Shahzeb" });
        var issued = PakistanTime.Today.AddDays(-10);

        await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 20_000m,
            Date = issued, CounterpartyFinanceAccountId = cash.Id
        }, 1);

        Assert.Equal(80_000m, (await accounts.GetByIdAsync(cash.Id)).CurrentBalance);
        Assert.Equal(20_000m, (await accounts.GetByIdAsync(holder.FinanceAccountId)).CurrentBalance);
        Assert.Equal(20_000m, (await finance.GetSummaryAsync(
            null, null, null, holder.FinanceAccountId)).AccountCurrentBalance);
        Assert.Equal(80_000m, (await finance.GetSummaryAsync(
            null, null, null, cash.Id)).AccountCurrentBalance);
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, issued, PakistanTime.Today)).TotalExpenses);

        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = holder.FinanceAccountId, CategoryId = lunch.Id,
            Amount = 2_900m, Date = issued.AddDays(1), Description = "Site lunch"
        }, 1);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = holder.FinanceAccountId, CategoryId = office.Id,
            Amount = 1_180m, Date = issued.AddDays(2), Description = "Office stationery"
        }, 1);

        var overview = await staff.GetOverviewAsync(true);
        var shahzeb = Assert.Single(overview.Holders);
        Assert.Equal(15_920m, shahzeb.CurrentBalance);
        Assert.Equal(issued, shahzeb.OutstandingSince);
        Assert.Equal(10, shahzeb.DaysOutstanding);
        Assert.Equal(15_920m, overview.TotalHeldByStaff);
        Assert.Equal(95_920m, overview.TrackedCompanyCash);
        Assert.Equal(4_080m, (await finance.GetProfitAndLossAsync(null, issued, PakistanTime.Today)).TotalExpenses);

        var sheet = await finance.GetBalanceSheetAsync(null, PakistanTime.Today);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(15_920m, Assert.Single(sheet.AssetGroups, g => g.Name == "Cash held by staff").Total);

        await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsReturned, Amount = 15_920m,
            Date = issued.AddDays(3), CounterpartyFinanceAccountId = cash.Id
        }, 1);

        overview = await staff.GetOverviewAsync(true);
        shahzeb = Assert.Single(overview.Holders);
        Assert.Equal(0m, shahzeb.CurrentBalance);
        Assert.Null(shahzeb.OutstandingSince);
        Assert.Null(shahzeb.DaysOutstanding);
        Assert.Equal(95_920m, overview.CashAndBankBalance);
        Assert.Equal(95_920m, overview.TrackedCompanyCash);
        Assert.Equal(4_080m, (await finance.GetProfitAndLossAsync(null, issued, PakistanTime.Today)).TotalExpenses);
    }

    [Fact]
    public async Task Overspending_BecomesAVisibleStaffPayable_AndCanBeSettled()
    {
        await using var context = Context();
        var bank = new FinanceAccount
        {
            Name = "Bank", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Bank, OpeningBalance = 1_000m, IsActive = true
        };
        context.FinanceAccounts.AddRange(bank, new FinanceAccount
        {
            Name = "Capital", AccountHolderName = "Owners",
            Type = FinanceAccountType.Capital, OpeningBalance = 1_000m, IsActive = true
        });
        var category = Category(1, "Site expense");
        context.ExpenseCategories.Add(category);
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);
        var staff = new StaffCashService(context, accounts);
        var finance = Finance(context, accounts);
        var holder = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Shahid" });
        var day = PakistanTime.Today.AddDays(-3);

        await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 100m, Date = day,
            CounterpartyFinanceAccountId = bank.Id
        }, null);
        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = holder.FinanceAccountId, CategoryId = category.Id,
            Amount = 150m, Date = day.AddDays(1)
        }, null);

        var overview = await staff.GetOverviewAsync(true);
        Assert.Equal(50m, overview.TotalOwedToStaff);
        Assert.Equal(-50m, overview.NetStaffBalance);
        Assert.Equal(850m, overview.TrackedCompanyCash);
        Assert.Equal(-50m, Assert.Single(overview.Holders).CurrentBalance);

        var sheet = await finance.GetBalanceSheetAsync(null, PakistanTime.Today);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(50m, Assert.Single(sheet.LiabilityGroups, g => g.Name == "Due to staff").Total);
        Assert.DoesNotContain(sheet.AssetGroups, g => g.Name == "Cash held by staff");

        await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 50m, Date = day.AddDays(2),
            CounterpartyFinanceAccountId = bank.Id, Note = "Reimburse overspend"
        }, null);
        overview = await staff.GetOverviewAsync(true);
        Assert.Equal(0m, Assert.Single(overview.Holders).CurrentBalance);
        Assert.Equal(0m, overview.TotalOwedToStaff);
        Assert.Equal(850m, overview.TrackedCompanyCash);
    }

    [Fact]
    public async Task SeveralPeopleKeepSeparateBalances_AndStaffFloatsAreExpenseOnlySources()
    {
        await using var context = Context();
        var cash = new FinanceAccount { Name = "Cash", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Cash, OpeningBalance = 500m, IsActive = true };
        var bank = new FinanceAccount { Name = "Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank, OpeningBalance = 500m, IsActive = true };
        context.FinanceAccounts.AddRange(cash, bank);
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);
        var staff = new StaffCashService(context, accounts);
        var a = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "A" });
        var b = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "B" });

        await staff.RecordTransferAsync(a.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 100m,
            Date = PakistanTime.Today, CounterpartyFinanceAccountId = cash.Id
        }, null);
        await staff.RecordTransferAsync(b.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 250m,
            Date = PakistanTime.Today, CounterpartyFinanceAccountId = bank.Id
        }, null);

        var overview = await staff.GetOverviewAsync(true);
        Assert.Equal(350m, overview.TotalHeldByStaff);
        Assert.Equal(100m, overview.Holders.Single(h => h.PersonName == "A").CurrentBalance);
        Assert.Equal(250m, overview.Holders.Single(h => h.PersonName == "B").CurrentBalance);
        Assert.Equal(1_000m, overview.TrackedCompanyCash);
        Assert.DoesNotContain(await accounts.GetOptionsAsync(false), x => x.Type == FinanceAccountType.StaffFloat);
        await accounts.EnsureExpenseSourceAsync(a.FinanceAccountId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.EnsureSelectableAsync(a.FinanceAccountId));
    }

    [Fact]
    public async Task TheStatementPagesInOrder_AndEveryPageAgreesWithTheWholeLedger()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Name = "Cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 1_000_000m, IsActive = true
        };
        context.FinanceAccounts.AddRange(cash, new FinanceAccount
        {
            Name = "Capital", AccountHolderName = "Owners",
            Type = FinanceAccountType.Capital, OpeningBalance = 1_000_000m, IsActive = true
        });
        var category = Category(1, "Site expense");
        context.ExpenseCategories.Add(category);
        await context.SaveChangesAsync();

        var accounts = new FinanceAccountService(context);
        var staff = new StaffCashService(context, accounts);
        var finance = Finance(context, accounts);
        var holder = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Bilal" });
        var start = PakistanTime.Today.AddDays(-20);

        // Cash out and cash spent on alternating days, so the balance moves in both directions.
        for (var i = 0; i < 6; i++)
        {
            await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
            {
                Type = StaffCashMovementType.FundsGiven, Amount = 1_000m,
                Date = start.AddDays(i * 2), CounterpartyFinanceAccountId = cash.Id
            }, 1);
            await finance.CreateExpenseAsync(new CreateExpenseDto
            {
                FinanceAccountId = holder.FinanceAccountId, CategoryId = category.Id,
                Amount = 400m, Date = start.AddDays(i * 2 + 1)
            }, 1);
        }

        var whole = await staff.GetStatementAsync(holder.FinanceAccountId, null, 200);
        Assert.Equal(12, whole.Items.Count);
        Assert.False(whole.HasMore);
        Assert.Equal(3_600m, whole.Holder.CurrentBalance);
        Assert.Equal(12, whole.Holder.TransactionCount);
        Assert.Equal(start, whole.Holder.OutstandingSince);

        // Newest first, and each row's balance is the previous row's with that movement undone.
        Assert.Equal(whole.Holder.CurrentBalance, whole.Items[0].RunningBalance);
        for (var i = 1; i < whole.Items.Count; i++)
            Assert.Equal(
                whole.Items[i - 1].RunningBalance - whole.Items[i - 1].Amount,
                whole.Items[i].RunningBalance);

        // A page is a window onto that statement, not a different one. Balances on page three are
        // only right if the rows above it were accounted for without being fetched.
        var first = await staff.GetStatementAsync(holder.FinanceAccountId, null, 5);
        Assert.NotNull(first.NextCursor);
        var second = await staff.GetStatementAsync(holder.FinanceAccountId, first.NextCursor, 5);
        Assert.NotNull(second.NextCursor);
        var third = await staff.GetStatementAsync(holder.FinanceAccountId, second.NextCursor, 5);
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(third.HasMore);

        Assert.Equal(
            whole.Items.Select(row => (row.RecordType, row.RecordId, row.RunningBalance)),
            first.Items.Concat(second.Items).Concat(third.Items)
                .Select(row => (row.RecordType, row.RecordId, row.RunningBalance)));
    }

    private static ExpenseCategory Category(int id, string name) => new()
    {
        Id = id, Name = name, Code = $"category-{id}", IsActive = true,
        IsWhtApplicable = false, DisplayOrder = id * 10
    };

    private static FinanceService Finance(AppDbContext context, FinanceAccountService accounts) =>
        new(context, new NoopStorage(), accounts, new WhtService(context, accounts),
            NullLogger<FinanceService>.Instance);

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
