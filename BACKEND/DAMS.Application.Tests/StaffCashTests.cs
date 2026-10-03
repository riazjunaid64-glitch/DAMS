using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.DTOs.ExpenseDtos;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
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
        var staff = new StaffCashService(context, accounts, TestAttachments.Writer());
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
        var staff = new StaffCashService(context, accounts, TestAttachments.Writer());
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
        var staff = new StaffCashService(context, accounts, TestAttachments.Writer());
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
        var staff = new StaffCashService(context, accounts, TestAttachments.Writer());
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

        var whole = await staff.GetStatementAsync(holder.FinanceAccountId, 0, 200);
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
        var first = await staff.GetStatementAsync(holder.FinanceAccountId, 0, 5);
        var second = await staff.GetStatementAsync(holder.FinanceAccountId, 5, 5);
        var third = await staff.GetStatementAsync(holder.FinanceAccountId, 10, 5);
        Assert.Equal([5, 5, 2], new[] { first.Items.Count, second.Items.Count, third.Items.Count });
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(third.HasMore);
        Assert.All(new[] { whole, first, second, third }, page => Assert.Equal(12, page.TotalCount));

        Assert.Equal(
            whole.Items.Select(row => (row.RecordType, row.RecordId, row.RunningBalance)),
            first.Items.Concat(second.Items).Concat(third.Items)
                .Select(row => (row.RecordType, row.RecordId, row.RunningBalance)));
    }

    [Fact]
    public async Task CashReturned_NeverExceedsWhatThePersonHolds_OnANewMovementOrACorrection()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Name = "Cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 100_000m, IsActive = true
        };
        context.FinanceAccounts.Add(cash);
        var category = Category(1, "Site expense");
        context.ExpenseCategories.Add(category);
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);
        var staff = new StaffCashService(context, accounts, TestAttachments.Writer());
        var finance = Finance(context, accounts);
        var owed = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Owed" });
        var holding = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Holding" });
        var day = PakistanTime.Today.AddDays(-3);
        SaveStaffCashTransferDto Dto(StaffCashMovementType type, decimal amount, string? token = null) => new()
        {
            Type = type, Amount = amount, Date = day, CounterpartyFinanceAccountId = cash.Id, ConcurrencyToken = token
        };

        // The company owes this person Rs 40: there is nothing to give back.
        await finance.CreateExpenseAsync(new CreateExpenseDto
        {
            FinanceAccountId = owed.FinanceAccountId, CategoryId = category.Id, Amount = 40m, Date = day
        }, 1);
        var refusedWhenOwed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.RecordTransferAsync(owed.FinanceAccountId, Dto(StaffCashMovementType.FundsReturned, 10m), 1));
        Assert.Equal("This person is not holding any company cash to return.", refusedWhenOwed.Message);

        // Holding Rs 10: Rs 50 cannot "come back", Rs 10 can.
        await staff.RecordTransferAsync(holding.FinanceAccountId, Dto(StaffCashMovementType.FundsGiven, 10m), 1);
        var tooMuch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            staff.RecordTransferAsync(holding.FinanceAccountId, Dto(StaffCashMovementType.FundsReturned, 50m), 1));
        Assert.Contains("more than the Rs 10.00", tooMuch.Message);
        var back = await staff.RecordTransferAsync(holding.FinanceAccountId, Dto(StaffCashMovementType.FundsReturned, 10m), 1);
        Assert.Equal(0m, (await staff.GetStatementAsync(holding.FinanceAccountId, 0, 20)).Holder.CurrentBalance);

        // Correcting that return to a larger amount is refused too; correcting it to the same or less is not.
        await Assert.ThrowsAsync<InvalidOperationException>(() => staff.UpdateTransferAsync(
            holding.FinanceAccountId, back.RecordId, Dto(StaffCashMovementType.FundsReturned, 11m, back.ConcurrencyToken), 1));
        await staff.UpdateTransferAsync(
            holding.FinanceAccountId, back.RecordId, Dto(StaffCashMovementType.FundsReturned, 4m, back.ConcurrencyToken), 1);
        Assert.Equal(6m, (await staff.GetStatementAsync(holding.FinanceAccountId, 0, 20)).Holder.CurrentBalance);

        // A correction cannot turn a handover into a return of cash that was never held.
        var given = await staff.RecordTransferAsync(owed.FinanceAccountId, Dto(StaffCashMovementType.FundsGiven, 100m), 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => staff.UpdateTransferAsync(
            owed.FinanceAccountId, given.RecordId, Dto(StaffCashMovementType.FundsReturned, 1m, given.ConcurrencyToken), 1));
    }

    [Fact]
    public async Task APageStaysConsistentWithItself_WhenAMovementIsRecordedBetweenTwoPageRequests()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Name = "Cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 100_000m, IsActive = true
        };
        context.FinanceAccounts.Add(cash);
        await context.SaveChangesAsync();
        var staff = new StaffCashService(context, new FinanceAccountService(context), TestAttachments.Writer());
        var holder = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Kamran" });
        async Task Give(int daysAgo) => await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
        {
            Type = StaffCashMovementType.FundsGiven, Amount = 1_000m,
            Date = PakistanTime.Today.AddDays(-daysAgo), CounterpartyFinanceAccountId = cash.Id
        }, 1);
        for (var i = 5; i >= 1; i--) await Give(i);

        var first = await staff.GetStatementAsync(holder.FinanceAccountId, 0, 3);
        await Give(0);
        var second = await staff.GetStatementAsync(holder.FinanceAccountId, 3, 3);

        // Whatever the shifted window holds, the page agrees with itself: its balances follow its own
        // rows, the total counts what exists, and there is a next page only when rows remain.
        Assert.Equal(6, second.TotalCount);
        Assert.Equal(3, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Equal(first.Items[^1].RecordId, second.Items[0].RecordId);
        Assert.Equal(first.Items[^1].RunningBalance, second.Items[0].RunningBalance);
        Assert.Equal(3_000m, second.Items[0].RunningBalance);
        Assert.Equal(1_000m, second.Items[^1].RunningBalance);
    }

    [Fact]
    public async Task APagePastTheEnd_IsEmptyButStillCountsEveryMovement_AndANegativeSkipStartsAtTheTop()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Name = "Cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 100_000m, IsActive = true
        };
        context.FinanceAccounts.Add(cash);
        await context.SaveChangesAsync();
        var staff = new StaffCashService(context, new FinanceAccountService(context), TestAttachments.Writer());
        var holder = await staff.CreateHolderAsync(new CreateStaffCashHolderDto { PersonName = "Kamran" });
        for (var i = 0; i < 3; i++)
            await staff.RecordTransferAsync(holder.FinanceAccountId, new SaveStaffCashTransferDto
            {
                Type = StaffCashMovementType.FundsGiven, Amount = 1_000m,
                Date = PakistanTime.Today.AddDays(-i), CounterpartyFinanceAccountId = cash.Id
            }, 1);

        var beyond = await staff.GetStatementAsync(holder.FinanceAccountId, 20, 20);
        Assert.Empty(beyond.Items);
        Assert.False(beyond.HasMore);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Equal(3_000m, beyond.Holder.CurrentBalance);

        var negative = await staff.GetStatementAsync(holder.FinanceAccountId, -4, 2);
        var top = await staff.GetStatementAsync(holder.FinanceAccountId, 0, 2);
        Assert.Equal(
            top.Items.Select(row => (row.RecordId, row.RunningBalance)),
            negative.Items.Select(row => (row.RecordId, row.RunningBalance)));
        Assert.True(negative.HasMore);
        Assert.Equal(3_000m, negative.Items[0].RunningBalance);
    }

    [Fact]
    public async Task APageThatStartsBetweenATransferAndAnExpenseSharingAnId_KeepsTheRightBalance()
    {
        await using var context = Context();
        var cash = new FinanceAccount
        {
            Id = 1, Name = "Cash", AccountHolderName = "Seven Ventures",
            Type = FinanceAccountType.Cash, OpeningBalance = 100_000m, IsActive = true
        };
        var staffFloat = new FinanceAccount
        {
            Id = 2, Name = "Ahmad - Staff float", AccountHolderName = "Ahmad",
            Type = FinanceAccountType.StaffFloat, OpeningBalance = 0m, IsActive = true
        };
        context.FinanceAccounts.AddRange(cash, staffFloat);
        // Same id, same day, same instant: only the ledger's tie-breakers tell these two apart,
        // and the boundary between page one and page two falls exactly between them.
        var day = PakistanTime.Today.AddDays(-2);
        var at = DateTime.UtcNow.AddDays(-2);
        context.StaffCashTransfers.Add(new StaffCashTransfer
        {
            Id = 7, StaffFinanceAccountId = staffFloat.Id, CounterpartyFinanceAccountId = cash.Id,
            Type = StaffCashMovementType.FundsGiven, Amount = 5_000m, Date = day, CreatedAt = at, UpdatedAt = at
        });
        context.Expenses.Add(new Expense
        {
            Id = 7, FinanceAccountId = staffFloat.Id, Category = "Fuel", Amount = 1_200m,
            Date = day, CreatedAt = at
        });
        await context.SaveChangesAsync();
        var staff = new StaffCashService(context, new FinanceAccountService(context), TestAttachments.Writer());

        var whole = await staff.GetStatementAsync(staffFloat.Id, 0, 20);
        var first = await staff.GetStatementAsync(staffFloat.Id, 0, 1);
        var second = await staff.GetStatementAsync(staffFloat.Id, 1, 1);

        Assert.Equal(
            [("Expense", 7, 3_800m), ("Transfer", 7, 5_000m)],
            whole.Items.Select(row => (row.RecordType, row.RecordId, row.RunningBalance)));
        Assert.Equal(
            whole.Items.Select(row => (row.RecordType, row.RecordId, row.RunningBalance)),
            first.Items.Concat(second.Items).Select(row => (row.RecordType, row.RecordId, row.RunningBalance)));
        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
    }

    [Theory]
    [InlineData(-3, 500, 0, 200)]
    [InlineData(40, 0, 40, 1)]
    public async Task TheStatementRoute_ClampsSkipAndTake(int skip, int take, int expectedSkip, int expectedTake)
    {
        var service = new RecordingStaffCash();
        var result = await new StaffCashController(service).Statement(9, skip, take);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((9, expectedSkip, expectedTake), service.Asked);
    }

    private sealed class RecordingStaffCash : IStaffCashService
    {
        public (int Id, int Skip, int Take) Asked { get; private set; }

        public Task<StaffCashStatementDto> GetStatementAsync(
            int staffFinanceAccountId, int skip, int take, CancellationToken cancellationToken = default)
        {
            Asked = (staffFinanceAccountId, skip, take);
            return Task.FromResult(new StaffCashStatementDto());
        }

        public Task<StaffCashOverviewDto> GetOverviewAsync(bool includeSettled, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StaffCashHolderDto> CreateHolderAsync(CreateStaffCashHolderDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StaffCashHistoryItemDto> RecordTransferAsync(int staffFinanceAccountId, SaveStaffCashTransferDto dto, int? userId,
            FinanceAttachmentUpload? attachment = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StaffCashHistoryItemDto> UpdateTransferAsync(int staffFinanceAccountId, int transferId, SaveStaffCashTransferDto dto,
            int? userId, FinanceAttachmentUpload? attachment = null, bool removeAttachment = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteTransferAsync(int staffFinanceAccountId, int transferId, string concurrencyToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FinanceAttachmentDownload> GetTransferAttachmentAsync(int staffFinanceAccountId, int transferId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RemoveTransferAttachmentAsync(int staffFinanceAccountId, int transferId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
