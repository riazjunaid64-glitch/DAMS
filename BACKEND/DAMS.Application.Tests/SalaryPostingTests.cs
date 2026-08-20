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
