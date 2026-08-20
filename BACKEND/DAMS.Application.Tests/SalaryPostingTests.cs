using DAMS.Application.DTOs.EmployeeDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Paying a salary is a finance posting, so it has to be a whole one.
/// <para>
/// Generating a salary writes a real <see cref="Expense"/> row — which is right, because salary is a
/// cost of the month it was paid in and belongs in the P&amp;L. But an expense is only half an entry
/// until it says which account the money left: the reports pair every expense with its paying
/// account, so one with none reduces profit with no matching credit anywhere. The Trial Balance and
/// Balance Sheet then go out by exactly the salary, and the formal reports can only describe it as
/// an "Unassigned expenses" imbalance rather than name the cause.
/// </para>
/// <para>
/// There is no accrual here on purpose: "generate salary" means the salary was paid, so the entry is
/// Dr Salary Expense / Cr Bank. A Salary Payable liability would be a different feature with a
/// different screen, and inventing one to absorb the missing credit would be an accounting decision
/// nobody asked for.
/// </para>
/// </summary>
public sealed class SalaryPostingTests
{
    private static readonly DateTime PayDay = new(2026, 8, 1);

    /// <summary>Codex finding 5, in full: the numbers it names, and both statements still balanced.</summary>
    [Fact]
    public async Task PayingASalary_DebitsTheExpense_CreditsTheBank_AndLeavesBothStatementsBalanced()
    {
        await using var context = Context();
        var world = await SeedAsync(context);

        var salary = await Employees(context).GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = PayDay, FinanceAccountId = world.BankId, Notes = "August payroll"
        }, adminUserId: 1);

        Assert.Equal(100_000m, salary.Amount);

        // The expense exists, at the salary amount, against the account that paid it.
        var expense = Assert.Single(await context.Expenses.AsNoTracking().ToListAsync());
        Assert.Equal(100_000m, expense.Amount);
        Assert.Equal(world.BankId, expense.FinanceAccountId);
        Assert.Equal("Salary", expense.Category);
        Assert.Equal(PayDay, expense.Date);
        Assert.Equal(expense.Id, (await context.EmployeeSalaries.AsNoTracking().SingleAsync()).ExpenseId);

        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);

        // Bank down by the salary; nothing withheld, so gross and net are the same figure.
        var bank = await accounts.GetByIdAsync(world.BankId);
        Assert.Equal(100_000m, bank.ExpensesPaid);
        Assert.Equal(400_000m, bank.CurrentBalance);

        var summary = await finance.GetSummaryAsync(null, null, null);
        Assert.Equal(100_000m, summary.TotalExpenses);
        Assert.Equal(-100_000m, summary.NetProfit);

        var pnl = await finance.GetProfitAndLossAsync(null, PayDay.AddDays(-1), PayDay.AddDays(1));
        Assert.Equal(100_000m, pnl.TotalExpenses);
        Assert.Equal(-100_000m, pnl.NetProfit);

        // The part that used to fail: an expense with no paying account has no credit side.
        var sheet = await finance.GetBalanceSheetAsync(null, PayDay.AddDays(1));
        Assert.True(sheet.IsBalanced,
            $"Balance sheet out by {sheet.Imbalance}: {string.Join(", ", sheet.UnbalancedAccounts ?? [])}");
        var trial = await finance.GetTrialBalanceAsync(null, new DateTime(2026, 8, 31), 0);
        Assert.True(Assert.Single(trial.ColumnBalanced));
    }

    [Fact]
    public async Task PayingASalary_WithNoAccount_IsRefused_AndRecordsNothing()
    {
        await using var context = Context();
        var world = await SeedAsync(context);

        var error = await Assert.ThrowsAsync<Exception>(() =>
            Employees(context).GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = PayDay
            }, adminUserId: 1));

        Assert.Contains("Paid From Account is required", error.Message);
        Assert.Empty(context.Expenses);
        Assert.Empty(context.EmployeeSalaries);
    }

    /// <summary>
    /// The account has to be one money can actually leave. A fixed-asset or receivable account would
    /// invert the entry's sign, which is the same rule the expense screens enforce — reused, not
    /// re-implemented, so payroll cannot drift away from it.
    /// </summary>
    [Fact]
    public async Task PayingASalary_FromAnAccountMoneyCannotLeave_IsRefused()
    {
        await using var context = Context();
        var world = await SeedAsync(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Employees(context).GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = PayDay, FinanceAccountId = world.CapitalAccountId
            }, adminUserId: 1));

        Assert.Contains("Expenses can only be paid from", error.Message);
        Assert.Empty(context.Expenses);
        Assert.Empty(context.EmployeeSalaries);
    }

    /// <summary>
    /// Editing a salary moves the linked expense's date too, so it takes the same bounds as writing
    /// one. A salary moved behind the committed baseline is counted twice — once inside the opening
    /// figures and again as a movement on top of them.
    /// </summary>
    [Fact]
    public async Task MovingASalaryBehindTheCommittedBaseline_IsRefused()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var employees = Employees(context);
        var salary = await employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = PayDay, FinanceAccountId = world.BankId
        }, adminUserId: 1);

        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = PayDay, IsCommitted = true, CommittedAt = DateTime.UtcNow, CommittedByUserId = 1
        });
        await context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            employees.UpdateSalaryAsync(salary.Id, new UpdateSalaryDto { PayDate = PayDay.AddDays(-10) }));

        Assert.Contains("before the committed opening balance date", error.Message);
        Assert.Equal(PayDay, (await context.Expenses.AsNoTracking().SingleAsync()).Date);
        Assert.Equal(PayDay, (await context.EmployeeSalaries.AsNoTracking().SingleAsync()).PayDate);
    }

    /// <summary>
    /// The payroll period and the day the cash moved are two different dates, and the posting takes
    /// the second one. The payroll screen used to send the 1st of the selected month whatever the
    /// admin did, so August payroll settled on the 20th was posted on 1 August: the totals balanced
    /// perfectly and the bank balance was wrong for every day in between.
    /// </summary>
    [Fact]
    public async Task PayrollSettledMidMonth_PostsTheCashOnTheDayItMoved_NotTheFirst()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var paidOn = new DateTime(2026, 8, 20);

        var salary = await Employees(context).GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = paidOn, PayMonth = 8, PayYear = 2026,
            FinanceAccountId = world.BankId
        }, adminUserId: 1);

        Assert.Equal(paidOn, salary.PayDate);
        Assert.Equal(8, salary.PayMonth);
        Assert.Equal(2026, salary.PayYear);
        Assert.Equal(paidOn, (await context.Expenses.AsNoTracking().SingleAsync()).Date);

        var finance = Finance(context);
        // The bank still holds the full float on the 19th and is down by the salary on the 20th.
        // Under the old behaviour both of these read 400,000.
        var before = await finance.GetBalanceSheetAsync(null, new DateTime(2026, 8, 19));
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, PayDay, new DateTime(2026, 8, 19))).TotalExpenses);
        Assert.True(before.IsBalanced);
        var after = await finance.GetProfitAndLossAsync(null, PayDay, new DateTime(2026, 8, 21));
        Assert.Equal(100_000m, after.TotalExpenses);
    }

    /// <summary>
    /// The case the old model could not express at all: a payroll period paid out in a LATER month.
    /// PayMonth/PayYear were derived from PayDate, so recording July's payroll on 5 August either
    /// dated the cash to 1 July (wrong bank history) or turned it into August's payroll (wrong
    /// period, and July silently free to be paid again).
    /// </summary>
    [Fact]
    public async Task PayrollPaidInTheFollowingMonth_KeepsItsOwnPeriod_AndDoesNotConsumeTheNewOne()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var employees = Employees(context);
        var paidOn = new DateTime(2026, 8, 5);

        var july = await employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = paidOn, PayMonth = 7, PayYear = 2026,
            FinanceAccountId = world.BankId
        }, adminUserId: 1);

        Assert.Equal(7, july.PayMonth);
        Assert.Equal(2026, july.PayYear);
        Assert.Equal(paidOn, july.PayDate);

        // The expense is dated when the money left, and names both facts so a report ordered by
        // posting date still explains itself.
        var expense = await context.Expenses.AsNoTracking().SingleAsync();
        Assert.Equal(paidOn, expense.Date);
        Assert.Contains("July 2026", expense.Description);
        Assert.Contains("paid 05 Aug 2026", expense.Description);

        // August's payroll is still owed and can still be recorded — the July run did not take it.
        var august = await employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = new DateTime(2026, 8, 20), PayMonth = 8, PayYear = 2026,
            FinanceAccountId = world.BankId
        }, adminUserId: 1);
        Assert.Equal(8, august.PayMonth);
        Assert.Equal(2, await context.EmployeeSalaries.CountAsync());

        // …and July still cannot be paid twice.
        var duplicate = await Assert.ThrowsAsync<Exception>(() =>
            employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = new DateTime(2026, 8, 6), PayMonth = 7, PayYear = 2026,
                FinanceAccountId = world.BankId
            }, adminUserId: 1));
        Assert.Contains("July 2026", duplicate.Message);
    }

    /// <summary>
    /// Correcting when the money actually left must not silently re-file the payroll period — that
    /// would move August's salary into September and free August up to be paid a second time.
    /// </summary>
    [Fact]
    public async Task CorrectingTheActualPaymentDate_LeavesThePayrollPeriodWhereItIs()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var employees = Employees(context);
        var salary = await employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = new DateTime(2026, 7, 31), PayMonth = 7, PayYear = 2026,
            FinanceAccountId = world.BankId
        }, adminUserId: 1);

        var moved = await employees.UpdateSalaryAsync(salary.Id, new UpdateSalaryDto
        {
            PayDate = new DateTime(2026, 8, 5)
        });

        Assert.Equal(new DateTime(2026, 8, 5), moved.PayDate);
        Assert.Equal(7, moved.PayMonth);
        Assert.Equal(2026, moved.PayYear);
        Assert.Equal(new DateTime(2026, 8, 5), (await context.Expenses.AsNoTracking().SingleAsync()).Date);

        // The period is still movable, deliberately and explicitly.
        var refiled = await employees.UpdateSalaryAsync(salary.Id, new UpdateSalaryDto
        {
            PayMonth = 8, PayYear = 2026
        });
        Assert.Equal(8, refiled.PayMonth);
        Assert.Equal(new DateTime(2026, 8, 5), refiled.PayDate);
    }

    /// <summary>Half a period is refused rather than guessed — a bare month would otherwise be filed
    /// under whatever year the payment happened to fall in.</summary>
    [Fact]
    public async Task APartOrOutOfRangePayrollPeriod_IsRefused()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var employees = Employees(context);

        var half = await Assert.ThrowsAsync<Exception>(() =>
            employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = PayDay, PayMonth = 7, FinanceAccountId = world.BankId
            }, adminUserId: 1));
        Assert.Contains("together", half.Message);

        var bad = await Assert.ThrowsAsync<Exception>(() =>
            employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
            {
                Amount = 100_000m, PayDate = PayDay, PayMonth = 13, PayYear = 2026,
                FinanceAccountId = world.BankId
            }, adminUserId: 1));
        Assert.Contains("between 1 and 12", bad.Message);
        Assert.Empty(context.EmployeeSalaries);

        // Omitting both still means "the month the payment falls in" — the old callers keep working.
        var derived = await employees.GenerateSalaryAsync(world.EmployeeId, new GenerateSalaryDto
        {
            Amount = 100_000m, PayDate = PayDay, FinanceAccountId = world.BankId
        }, adminUserId: 1);
        Assert.Equal(8, derived.PayMonth);
        Assert.Equal(2026, derived.PayYear);
    }

    // ── Helpers ──

    private sealed record World(int EmployeeId, int BankId, int CapitalAccountId);

    private static async Task<World> SeedAsync(AppDbContext context)
    {
        // Opening capital of 500,000 held in the bank: a starting position that balances, so any
        // imbalance the salary introduces is the salary's own.
        var bank = new FinanceAccount
        {
            Name = "Payroll Bank", AccountHolderName = "Seven Ventures", Type = FinanceAccountType.Bank,
            OpeningBalance = 500_000m, IsActive = true
        };
        var capital = new FinanceAccount
        {
            Name = "Partner Capital", AccountHolderName = "Partner One", Type = FinanceAccountType.Capital,
            OpeningBalance = 500_000m, IsActive = true
        };
        var employee = new Employee
        {
            FullName = "Site Engineer", JobTitle = "Engineer", Department = "Construction",
            Phone = "03007778888", Salary = 100_000m, JoinDate = new DateTime(2026, 1, 1),
            Status = EmployeeStatus.Active
        };
        context.AddRange(bank, capital, employee);
        await context.SaveChangesAsync();
        return new World(employee.Id, bank.Id, capital.Id);
    }

    private static EmployeeService Employees(AppDbContext context) =>
        new(context, new FinanceAccountService(context));

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // Salary generation writes the expense and the salary in one transaction, which the
            // in-memory store cannot provide. Same allowance the lead harness makes: what is under
            // test here is the entry the two rows form, not the atomicity of writing them.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
