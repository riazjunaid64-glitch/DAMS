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

public sealed class TrialBalanceBatchTests
{
    [Fact]
    public async Task MultiColumnBatch_MatchesIndependentColumns_AcrossBaseline_GlobalAndProject()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var bank = new FinanceAccount
        {
            Name = "Batch Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank,
            OpeningBalance = 1_000m, IsActive = true
        };
        var capitalAccount = new FinanceAccount
        {
            Name = "Batch Capital", AccountHolderName = "Partner", Type = FinanceAccountType.Capital,
            OpeningBalance = 1_000m, IsActive = true
        };
        var taxPayable = new FinanceAccount
        {
            Name = "Batch Tax Payable", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability,
            SystemRole = FinanceSystemAccountRole.TaxPayable, IsActive = true
        };
        var projectA = new Project { ProjectName = "Batch A", Location = "Karachi", CreatedById = 1 };
        var projectB = new Project { ProjectName = "Batch B", Location = "Lahore", CreatedById = 1 };
        var incomeCategory = new RevenueCategory
        {
            Name = "Batch Income", Code = "batch_income", DisplayOrder = 10, IsActive = true
        };
        var expenseCategory = new ExpenseCategory
        {
            Name = "Batch Expense", Code = "batch_expense", DisplayOrder = 10, IsActive = true
        };
        var partner = new CapitalPartner
        {
            Name = "Batch Partner", ProfitSharePercent = 100m, FinanceAccount = capitalAccount
        };
        context.AddRange(bank, taxPayable, projectA, projectB, incomeCategory, expenseCategory, partner);
        await context.SaveChangesAsync();

        // A mid-month baseline deliberately falls between the February and March month-end
        // columns. Physical account movements remain all-time, while virtual P&L accumulation
        // resets on the baseline and project reports never inherit global opening balances.
        context.OpeningBalanceSets.Add(new OpeningBalanceSet
        {
            AsAtDate = new DateTime(2026, 3, 15), IsCommitted = true,
            CommittedAt = new DateTime(2026, 3, 14, 19, 0, 0, DateTimeKind.Utc)
        });
        context.ManualRevenues.AddRange(
            Income(projectA.Id, bank.Id, incomeCategory.Id, 100m, new DateTime(2026, 1, 10)),
            Income(projectB.Id, bank.Id, incomeCategory.Id, 200m, new DateTime(2026, 2, 10)),
            Income(projectA.Id, bank.Id, incomeCategory.Id, 300m, new DateTime(2026, 3, 20)),
            Income(projectB.Id, bank.Id, incomeCategory.Id, 400m, new DateTime(2026, 4, 20)),
            Income(projectA.Id, bank.Id, incomeCategory.Id, 500m, new DateTime(2026, 5, 20)));
        context.Expenses.AddRange(
            Expense(projectA.Id, bank.Id, expenseCategory.Id, 40m, 4m, new DateTime(2026, 2, 12)),
            Expense(projectB.Id, bank.Id, expenseCategory.Id, 80m, 8m, new DateTime(2026, 4, 12)));
        // Deliberately unusual but legal: using the role account as the payment account exercises
        // the legacy additive semantics (generic account movement plus role-derived movement).
        context.WhtDeposits.Add(new WhtDeposit
        {
            FinanceAccountId = taxPayable.Id, Amount = 2m, DepositDate = new DateTime(2026, 4, 15)
        });

        // Equal allocations are an important row-presence edge: the legacy report emits the
        // allocation row because both sides exist even though its displayed net cells are zero.
        context.CapitalTransactions.AddRange(
            Capital(partner.Id, CapitalTransactionType.ProfitShare, 50m, new DateTime(2026, 3, 25)),
            Capital(partner.Id, CapitalTransactionType.LossShare, 50m, new DateTime(2026, 3, 25)),
            Capital(partner.Id, CapitalTransactionType.Contribution, 100m,
                new DateTime(2026, 4, 25), bank.Id));
        await context.SaveChangesAsync();

        var finance = Finance(context);
        var asAt = new DateTime(2026, 5, 31);
        var global = await finance.GetTrialBalanceAsync(null, asAt, 4);
        var project = await finance.GetTrialBalanceAsync(projectA.Id, asAt, 4);

        await AssertMatchesIndependentColumns(finance, null, global);
        await AssertMatchesIndependentColumns(finance, projectA.Id, project);

        Assert.Equal(
            [new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), new DateTime(2026, 3, 31),
                new DateTime(2026, 4, 30), new DateTime(2026, 5, 31)],
            global.ColumnDates);

        var globalIncome = Assert.Single(global.Rows, row => row.AccountName == "Batch Income");
        Assert.Equal([100m, 300m, 300m, 700m, 1_200m], globalIncome.CreditBalances);
        var projectIncome = Assert.Single(project.Rows, row => row.AccountName == "Batch Income");
        Assert.Equal([100m, 100m, 300m, 300m, 800m], projectIncome.CreditBalances);

        var globalBank = Assert.Single(global.Rows, row => row.AccountKey == $"A:{bank.Id}");
        Assert.Equal(100m, globalBank.DebitBalances[0]);
        Assert.Equal(264m, globalBank.DebitBalances[1]);
        Assert.Equal(1_564m, globalBank.DebitBalances[2]);
        var projectBank = Assert.Single(project.Rows, row => row.AccountKey == $"A:{bank.Id}");
        Assert.Equal(64m, projectBank.DebitBalances[1]);
        Assert.Equal(364m, projectBank.DebitBalances[2]);
        var globalTax = Assert.Single(global.Rows, row => row.AccountKey == $"A:{taxPayable.Id}");
        Assert.Equal(8m, globalTax.CreditBalances[3]); // 12 withheld - 2 role clearing - 2 generic outflow
        var projectTax = Assert.Single(project.Rows, row => row.AccountKey == $"A:{taxPayable.Id}");
        Assert.Equal(4m, projectTax.CreditBalances[3]); // project A's WHT only; global deposit is excluded

        var allocated = Assert.Single(global.Rows, row => row.AccountKey == "EQ:allocated");
        Assert.All(allocated.DebitBalances, amount => Assert.Equal(0m, amount));
        Assert.All(allocated.CreditBalances, amount => Assert.Equal(0m, amount));
        Assert.DoesNotContain(project.Rows, row => row.AccountKey == "EQ:allocated");
    }

    private static ManualRevenue Income(
        int projectId, int accountId, int categoryId, decimal amount, DateTime date) => new()
    {
        ProjectId = projectId,
        FinanceAccountId = accountId,
        RevenueCategoryId = categoryId,
        RevenueType = "Batch Income",
        RevenueTypeName = "Batch Income",
        Amount = amount,
        Date = date
    };

    private static Expense Expense(
        int projectId, int accountId, int categoryId, decimal amount, decimal wht, DateTime date) => new()
    {
        ProjectId = projectId,
        FinanceAccountId = accountId,
        CategoryId = categoryId,
        Category = "Batch Expense",
        Amount = amount,
        WhtAmount = wht,
        Date = date
    };

    private static CapitalTransaction Capital(
        int partnerId, CapitalTransactionType type, decimal amount, DateTime date,
        int? financeAccountId = null) => new()
    {
        CapitalPartnerId = partnerId,
        Type = type,
        Amount = amount,
        Date = date,
        FinanceAccountId = financeAccountId
    };

    private static async Task AssertMatchesIndependentColumns(
        FinanceService finance, int? projectId, TrialBalanceDto batch)
    {
        var expectedRows = new Dictionary<string, TrialBalanceRowDto>(StringComparer.Ordinal);
        for (var column = 0; column < batch.ColumnDates.Count; column++)
        {
            var single = await finance.GetTrialBalanceAsync(projectId, batch.ColumnDates[column], 0);
            Assert.Equal(single.TotalDebit, batch.ColumnDebitTotals[column]);
            Assert.Equal(single.TotalCredit, batch.ColumnCreditTotals[column]);
            Assert.Equal(single.IsBalanced, batch.ColumnBalanced[column]);

            foreach (var row in single.Rows)
                expectedRows.TryAdd(row.AccountKey, row);

            foreach (var batchRow in batch.Rows)
            {
                var singleRow = single.Rows.SingleOrDefault(row => row.AccountKey == batchRow.AccountKey);
                Assert.Equal(singleRow?.Debit ?? 0m, batchRow.DebitBalances[column]);
                Assert.Equal(singleRow?.Credit ?? 0m, batchRow.CreditBalances[column]);
            }
        }

        var expectedOrder = expectedRows.Values
            .OrderBy(row => row.AccountId < 0 ? 1 : 0)
            .ThenBy(row => row.Type)
            .ThenBy(row => row.AccountName)
            .Select(row => row.AccountKey);
        Assert.Equal(expectedOrder, batch.Rows.Select(row => row.AccountKey));
    }

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(
            Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");

        public Task<Stream?> OpenReadAsync(
            string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(
            string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
