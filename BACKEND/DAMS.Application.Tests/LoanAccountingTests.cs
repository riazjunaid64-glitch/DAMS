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

public sealed class LoanAccountingTests
{
    private static readonly DateTime DrawDate = new(2026, 8, 1);
    private static readonly DateTime RepayDate = new(2026, 8, 10);

    [Fact]
    public async Task DrawdownAndSplitRepayment_MoveTheRightAccounts_AndOnlyInterestMovesProfit()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var loans = Loans(context);
        var finance = Finance(context);
        var accounts = new FinanceAccountService(context);
        var loan = await loans.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));

        await loans.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Drawdown, 5_000_000m, 0m, DrawDate, world.Hbl.Id), 1);

        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(5_000_000m, (await accounts.GetByIdAsync(world.LoanLiability.Id)).CurrentBalance);
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, DrawDate, RepayDate)).NetProfit);
        Assert.True((await finance.GetBalanceSheetAsync(null, DrawDate)).IsBalanced);

        await loans.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Repayment, 500_000m, 50_000m, RepayDate, world.Hbl.Id), 1);

        Assert.Equal(4_450_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(4_500_000m, (await accounts.GetByIdAsync(world.LoanLiability.Id)).CurrentBalance);
        var pnl = await finance.GetProfitAndLossAsync(null, DrawDate, RepayDate);
        Assert.Equal(50_000m, pnl.TotalExpenses);
        Assert.Equal(-50_000m, pnl.NetProfit);
        var interest = Assert.Single(pnl.ExpenseLines);
        Assert.Equal("Loan Interest", interest.Name);
        Assert.Equal(50_000m, interest.Amount);

        var summary = await finance.GetSummaryAsync(null, DrawDate, RepayDate);
        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(50_000m, summary.TotalExpenses);
        Assert.Equal(-50_000m, summary.NetProfit);
        var bankSummary = await finance.GetSummaryAsync(null, DrawDate, RepayDate, world.Hbl.Id);
        Assert.Equal(4_450_000m, bankSummary.AccountCurrentBalance);
        Assert.Equal(4_450_000m, bankSummary.AccountNetMovement);
        Assert.Equal(50_000m, bankSummary.TotalExpenses);
        Assert.Equal(-550_000m, (await finance.GetSummaryAsync(null, RepayDate, RepayDate, world.Hbl.Id)).AccountNetMovement);
        var sheet = await finance.GetBalanceSheetAsync(null, RepayDate);
        Assert.True(sheet.IsBalanced);
        Assert.Equal(4_450_000m, sheet.TotalAssets);
        Assert.Equal(4_500_000m, sheet.TotalLiabilities);
        Assert.Equal(-50_000m, sheet.RetainedProfit);
        Assert.True(Assert.Single((await finance.GetTrialBalanceAsync(null, RepayDate, 0)).ColumnBalanced));
    }

    [Fact]
    public async Task PrincipalOnlyInterestOnlyAndMultipleLoans_AreIndependentAndHaveCorrectRunningBalances()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var secondLiability = new FinanceAccount
        {
            Name = "Director Loan A/C", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability, IsActive = true
        };
        context.FinanceAccounts.Add(secondLiability);
        await context.SaveChangesAsync();
        var service = Loans(context);
        var first = await service.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));
        var second = await service.CreateAsync(LoanInput("Director Loan", secondLiability.Id));
        await service.RecordTransactionAsync(first.Id, Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DrawDate, world.Hbl.Id), 1);
        await service.RecordTransactionAsync(first.Id, Movement(LoanTransactionType.Repayment, 400m, 0m, DrawDate.AddDays(1), world.Hbl.Id), 1);
        await service.RecordTransactionAsync(first.Id, Movement(LoanTransactionType.Repayment, 0m, 50m, DrawDate.AddDays(2), world.Hbl.Id), 1);
        await service.RecordTransactionAsync(second.Id, Movement(LoanTransactionType.Drawdown, 200m, 0m, DrawDate, world.Cash.Id), 1);

        var rows = await service.GetAllAsync(false);
        Assert.Equal(2, rows.Count);
        Assert.Equal(600m, rows.Single(l => l.Id == first.Id).CurrentBalance);
        Assert.Equal(50m, rows.Single(l => l.Id == first.Id).InterestPaid);
        Assert.Equal(200m, rows.Single(l => l.Id == second.Id).CurrentBalance);

        var statement = await service.GetStatementAsync(first.Id, 0, 100);
        Assert.Equal(3, statement.Items.Count);
        Assert.Equal(600m, statement.Items[0].RunningBalance); // interest-only, newest
        Assert.Equal(600m, statement.Items[1].RunningBalance); // principal repayment
        Assert.Equal(1_000m, statement.Items[2].RunningBalance); // drawdown
        Assert.Equal(0m, statement.Items[0].PrincipalAmount);
        Assert.Equal(50m, statement.Items[0].InterestAmount);
    }

    [Fact]
    public async Task CorrectingAndDeletingARepayment_RecalculatesBothSidesAndProfit()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Loans(context);
        var accounts = new FinanceAccountService(context);
        var finance = Finance(context);
        var loan = await service.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));
        var drawdown = await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Drawdown, 5_000m, 0m, DrawDate, world.Hbl.Id), 1);
        var repayment = await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Repayment, 500m, 50m, RepayDate, world.Hbl.Id), 1);

        var invalidCorrection = Movement(LoanTransactionType.Repayment, 6_000m, 40m, RepayDate, world.Cash.Id);
        invalidCorrection.ConcurrencyToken = repayment.ConcurrencyToken;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTransactionAsync(loan.Id, repayment.Id, invalidCorrection, 7));
        Assert.Equal(500m, (await context.LoanTransactions.SingleAsync(t => t.Id == repayment.Id)).PrincipalAmount);

        var correction = Movement(LoanTransactionType.Repayment, 400m, 40m, RepayDate, world.Cash.Id);
        correction.ConcurrencyToken = repayment.ConcurrencyToken;
        repayment = await service.UpdateTransactionAsync(loan.Id, repayment.Id, correction, 7);

        Assert.Equal(5_000m, (await accounts.GetByIdAsync(world.Hbl.Id)).CurrentBalance);
        Assert.Equal(-440m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(4_600m, (await accounts.GetByIdAsync(world.LoanLiability.Id)).CurrentBalance);
        Assert.Equal(40m, (await finance.GetProfitAndLossAsync(null, DrawDate, RepayDate)).TotalExpenses);

        await service.DeleteTransactionAsync(loan.Id, repayment.Id, repayment.ConcurrencyToken);
        Assert.Equal(0m, (await accounts.GetByIdAsync(world.Cash.Id)).CurrentBalance);
        Assert.Equal(5_000m, (await accounts.GetByIdAsync(world.LoanLiability.Id)).CurrentBalance);
        Assert.Equal(0m, (await finance.GetProfitAndLossAsync(null, DrawDate, RepayDate)).TotalExpenses);

        // Removing the funding while a later principal repayment remains would invent a negative loan.
        var principal = await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Repayment, 100m, 0m, RepayDate, world.Hbl.Id), 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteTransactionAsync(loan.Id, drawdown.Id, drawdown.ConcurrencyToken));
        Assert.Contains("exceed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task InvalidShapesAccountsDatesAndOverpayments_AreRejectedWithoutPartialWrites()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Loans(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(LoanInput("Wrong", world.Hbl.Id)));
        var loan = await service.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Drawdown, 1_000m, 1m, DrawDate, world.Hbl.Id), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Repayment, 0m, 0m, RepayDate, world.Hbl.Id), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DrawDate, world.LoanLiability.Id), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Drawdown, 1_000.001m, 0m, DrawDate, world.Hbl.Id), 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DateTime.Today.AddYears(2), world.Hbl.Id), 1));

        await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DrawDate, world.Hbl.Id), 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Repayment, 1_001m, 0m, RepayDate, world.Hbl.Id), 1));
        Assert.Single(context.LoanTransactions);
    }

    [Fact]
    public async Task LoanAccountsAppearInBothLedgers_CannotBeDeletedAndDoNotEnterProjectProfit()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var project = new Project { ProjectName = "Project A", Location = "Lahore" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        var service = Loans(context);
        var accounts = new FinanceAccountService(context);
        var loan = await service.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));
        await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DrawDate, world.Hbl.Id), 1);
        await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Repayment, 100m, 10m, RepayDate, world.Hbl.Id), 1);

        var bankRows = (await accounts.GetTransactionsAsync(world.Hbl.Id, 0, 20)).Items;
        Assert.Contains(bankRows, row => row.Kind == "Loan drawdown" && row.Amount == 1_000m);
        Assert.Contains(bankRows, row => row.Kind == "Loan repayment" && row.Amount == -110m);
        var liabilityRows = (await accounts.GetTransactionsAsync(world.LoanLiability.Id, 0, 20)).Items;
        Assert.Contains(liabilityRows, row => row.Kind == "Loan principal drawn" && row.Amount == 1_000m);
        Assert.Contains(liabilityRows, row => row.Kind == "Loan principal repaid" && row.Amount == -100m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(world.Hbl.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteUnusedAsync(world.LoanLiability.Id));

        var projectPnl = await Finance(context).GetProfitAndLossAsync(project.Id, DrawDate, RepayDate);
        Assert.Equal(0m, projectPnl.TotalExpenses);
    }

    [Fact]
    public async Task OutstandingLoanCannotBeArchived_AndItsActiveLiabilityAccountIsProtected()
    {
        await using var context = Context();
        var world = await SeedAsync(context);
        var service = Loans(context);
        var accounts = new FinanceAccountService(context);
        var loan = await service.CreateAsync(LoanInput("HBL Term Loan", world.LoanLiability.Id));
        await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Drawdown, 1_000m, 0m, DrawDate, world.Hbl.Id), 1);

        var archive = LoanInput(loan.Name, loan.FinanceAccountId);
        archive.IsActive = false;
        archive.ConcurrencyToken = loan.ConcurrencyToken;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(loan.Id, archive));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetActiveAsync(world.LoanLiability.Id, false, ""));

        await service.RecordTransactionAsync(loan.Id, Movement(LoanTransactionType.Repayment, 1_000m, 0m, RepayDate, world.Hbl.Id), 1);
        loan = (await service.GetAllAsync(true)).Single();
        archive.ConcurrencyToken = loan.ConcurrencyToken;
        loan = await service.UpdateAsync(loan.Id, archive);
        Assert.False(loan.IsActive);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTransactionAsync(loan.Id,
            Movement(LoanTransactionType.Repayment, 0m, 10m, RepayDate, world.Hbl.Id), 1));

        var settledRepayment = (await service.GetStatementAsync(loan.Id, 0, 10)).Items
            .Single(t => t.Type == LoanTransactionType.Repayment);
        var correction = Movement(LoanTransactionType.Repayment, 900m, 0m, RepayDate, world.Hbl.Id);
        correction.ConcurrencyToken = settledRepayment.ConcurrencyToken;
        var correctionError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTransactionAsync(loan.Id, settledRepayment.Id, correction, 1));
        Assert.Contains("Reactivate", correctionError.Message);

        var deleteError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteTransactionAsync(loan.Id, settledRepayment.Id, settledRepayment.ConcurrencyToken));
        Assert.Contains("Reactivate", deleteError.Message);
        Assert.Equal(0m, (await service.GetAllAsync(true)).Single().CurrentBalance);
    }

    private sealed record World(FinanceAccount Hbl, FinanceAccount Cash, FinanceAccount LoanLiability);

    private static async Task<World> SeedAsync(AppDbContext context)
    {
        var hbl = new FinanceAccount { Name = "HBL Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank, IsActive = true };
        var cash = new FinanceAccount { Name = "Cash", AccountHolderName = "DAMS", Type = FinanceAccountType.Cash, IsActive = true };
        var liability = new FinanceAccount { Name = "Loan A/C", AccountHolderName = "DAMS", Type = FinanceAccountType.Liability, IsActive = true };
        context.AddRange(hbl, cash, liability);
        await context.SaveChangesAsync();
        return new World(hbl, cash, liability);
    }

    private static SaveLoanDto LoanInput(string name, int accountId) => new()
    {
        Name = name, LenderName = "HBL", FinanceAccountId = accountId, IsActive = true
    };

    private static SaveLoanTransactionDto Movement(LoanTransactionType type, decimal principal, decimal interest,
        DateTime date, int accountId) => new()
    {
        Type = type, PrincipalAmount = principal, InterestAmount = interest, Date = date,
        FinanceAccountId = accountId, Reference = "BANK-STMT"
    };

    private static LoanService Loans(AppDbContext context) => new(context, new FinanceAccountService(context), TestAttachments.Writer());

    private static FinanceService Finance(AppDbContext context)
    {
        var accounts = new FinanceAccountService(context);
        return new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
