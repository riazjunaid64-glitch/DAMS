using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using DAMS.Application.DTOs.FinanceDtos;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Domain.Entities;
using DAMS.Domain.Enums;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace DAMS.Application.Tests;

public sealed class AccountPerformanceSqlTests(ITestOutputHelper output)
{
    [SqlServerFact]
    public async Task AccountReads_ReduceDatabaseWork_AndPreserveFinancialResultsAsDataGrows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString).AddInterceptors(counter).Options;
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
        var day = new DateTime(2026, 8, 20);
        var bank = new FinanceAccount
        {
            Name = "Selected Bank", AccountHolderName = "DAMS", Type = FinanceAccountType.Bank,
            OpeningBalance = 1000m, IsActive = true
        };
        context.FinanceAccounts.Add(bank);
        await context.SaveChangesAsync();
        var initialAccountCount = await context.FinanceAccounts.CountAsync();
        context.ManualRevenues.Add(new ManualRevenue
        {
            FinanceAccountId = bank.Id, Date = day.AddDays(-1), Amount = 200m, RevenueType = "Prior"
        });
        for (var i = 0; i < 30; i++)
        {
            context.ManualRevenues.Add(new ManualRevenue
            {
                FinanceAccountId = bank.Id, Date = day, CreatedAt = day.AddMinutes(i),
                Amount = 10m, RevenueType = $"Income {i}", Reference = $"R-{i}", Description = "Historical income"
            });
            context.Expenses.Add(new Expense
            {
                FinanceAccountId = bank.Id, Date = day, CreatedAt = day.AddMinutes(i),
                Amount = 5m, WhtAmount = 1m, Category = "Office", Vendor = $"Supplier {i}"
            });
        }
        await context.SaveChangesAsync();
        var accounts = new FinanceAccountService(context);
        var finance = new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);

        // Exercise the exact normal UI request as well as the filtered, offset overload below.
        // Asking the shared query for balances reproduces the former history calculation.
        counter.Reset();
        var legacyInitial = await HistoryWithUnusedBalancesAsync(accounts, bank.Id, 0, 5);
        var legacyInitialCommands = counter.Commands.Count;
        counter.Reset();
        var initial = await accounts.GetTransactionsAsync(bank.Id, 0, 5);
        var initialCommands = counter.Commands.Count;
        Assert.Equal(legacyInitialCommands - 1, initialCommands);
        Assert.Equal(JsonSerializer.Serialize(legacyInitial.Items), JsonSerializer.Serialize(initial.Items));
        Assert.Equal(legacyInitial.HasMore, initial.HasMore);
        output.WriteLine($"Initial UI history (no date filter, skip=0): {legacyInitialCommands} -> {initialCommands} commands.");

        // The former history path computed all three ledger sums and discarded them.
        counter.Reset();
        var ledger = await accounts.GetTransactionLedgerSliceAsync(bank.Id, null, day, day, 10, 5);
        var ledgerCommands = counter.Commands.Count;
        Assert.Equal(1200m, ledger.OpeningNormalBalance);
        Assert.Equal(180m, ledger.PeriodNormalMovement);
        Assert.Equal(30m, ledger.NormalMovementBeforePage);
        counter.Reset();
        var history = await accounts.GetTransactionsAsync(bank.Id, null, day, day, 10, 5);
        var historyCommands = counter.Commands.Count;
        Assert.Equal(5, history.Items.Count);
        Assert.True(history.HasMore);
        Assert.Equal(ledgerCommands - 3, historyCommands);
        Assert.DoesNotContain(counter.Commands, sql => sql.Contains("SUM(", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(counter.Commands, sql => sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("FETCH NEXT", StringComparison.OrdinalIgnoreCase));
        var completeLedger = await accounts.GetTransactionLedgerSliceAsync(bank.Id, null, day, day, 0, int.MaxValue);
        Assert.Equal(JsonSerializer.Serialize(completeLedger.Items.AsEnumerable().Reverse().Skip(10).Take(5)),
            JsonSerializer.Serialize(history.Items));
        output.WriteLine($"Transaction page: {ledgerCommands} former ledger commands -> {historyCommands}; rows returned: {history.Items.Count}.");

        counter.Reset();
        var oldPage = await accounts.GetPageAsync(null, null, null, null, 0, 200);
        var oldOverview = await accounts.GetOverviewAsync();
        var separateCommands = counter.Commands.Count;
        counter.Reset();
        var combined = await accounts.GetPageWithOverviewAsync(null, null, null, null, 0, 200);
        var combinedCommands = counter.Commands.Count;
        Assert.True(combinedCommands < separateCommands);
        Assert.Equal(JsonSerializer.Serialize(oldPage.Items), JsonSerializer.Serialize(combined.Items));
        Assert.Equal(JsonSerializer.Serialize(oldOverview), JsonSerializer.Serialize(combined.Overview));
        Assert.Single(counter.Commands, sql => sql.Contains("GROUP BY") && sql.Contains("UNION ALL"));
        output.WriteLine($"Accounts page: {separateCommands} separate-load commands -> {combinedCommands} combined-load commands.");

        counter.Reset();
        var smallSummary = await finance.GetSummaryAsync(null, day, day, bank.Id);
        var smallSummaryCommands = counter.Commands.Count;
        Assert.Equal(1380m, smallSummary.AccountCurrentBalance);
        Assert.Equal(180m, smallSummary.AccountNetMovement);

        // A history page should still fetch its sentinel page only after its own history grows.
        for (var i = 0; i < 500; i++)
            context.ManualRevenues.Add(new ManualRevenue
            {
                FinanceAccountId = bank.Id, Date = day.AddDays(-2), Amount = 1m, RevenueType = "Older history"
            });
        for (var i = 0; i < 250; i++)
        {
            var unrelated = new FinanceAccount
            {
                Name = $"Unrelated {i:D3}", Type = FinanceAccountType.Bank, AccountHolderName = "Another holder",
                OpeningBalance = i, IsActive = i % 2 == 0
            };
            context.FinanceAccounts.Add(unrelated);
            context.ManualRevenues.Add(new ManualRevenue
            {
                FinanceAccount = unrelated, Date = day, Amount = 9999m, RevenueType = "Unrelated income"
            });
        }
        await context.SaveChangesAsync();
        counter.Reset();
        var largeHistory = await accounts.GetTransactionsAsync(bank.Id, null, day, day, 10, 5);
        Assert.Equal(historyCommands, counter.Commands.Count);
        Assert.Equal(JsonSerializer.Serialize(history), JsonSerializer.Serialize(largeHistory));
        counter.Reset();
        var largeInitial = await accounts.GetTransactionsAsync(bank.Id, 0, 5);
        Assert.Equal(initialCommands, counter.Commands.Count);
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(largeInitial));
        counter.Reset();
        var largeSummary = await finance.GetSummaryAsync(null, day, day, bank.Id);
        Assert.Equal(smallSummaryCommands, counter.Commands.Count);
        Assert.Equal(smallSummary.AccountCurrentBalance + 500m, largeSummary.AccountCurrentBalance);
        Assert.Equal(smallSummary.AccountNetMovement, largeSummary.AccountNetMovement);

        // Provider collation and filtering stay identical, while the summary still spans the
        // complete chart even though this request only returns a small, filtered page.
        var filteredPage = await accounts.GetPageAsync("unRELATED", FinanceAccountType.Bank, " another holder ", true, 3, 17);
        var globalOverview = await accounts.GetOverviewAsync();
        counter.Reset();
        var filteredCombined = await accounts.GetPageWithOverviewAsync("unRELATED", FinanceAccountType.Bank, " another holder ", true, 3, 17);
        Assert.Equal(JsonSerializer.Serialize(filteredPage.Items), JsonSerializer.Serialize(filteredCombined.Items));
        Assert.Equal(filteredPage.HasMore, filteredCombined.HasMore);
        Assert.Equal(JsonSerializer.Serialize(globalOverview), JsonSerializer.Serialize(filteredCombined.Overview));
        Assert.Single(counter.Commands, sql => sql.Contains("GROUP BY") && sql.Contains("UNION ALL"));

        // Inspect the snapshot operation itself: output AND database source predicates must be
        // account-scoped. Flat query counts alone would miss loading every unrelated balance.
        counter.Reset();
        var selectedSnapshots = await SnapshotsAsync(finance, day, bank.Id);
        Assert.Single(selectedSnapshots);
        var selectedMovementSql = Assert.Single(counter.Commands,
            sql => sql.Contains("GROUP BY") && sql.Contains("UNION ALL"));
        Assert.All(selectedMovementSql.Split("UNION ALL", StringSplitOptions.None), branch =>
            Assert.Contains("accountId", branch, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(counter.Commands, sql => sql.Contains("FROM [FinanceAccounts]")
            && sql.Contains("WHERE") && sql.Contains("accountId", StringComparison.OrdinalIgnoreCase));
        var allSnapshots = await SnapshotsAsync(finance, day, null);
        Assert.Equal(initialAccountCount + 250, allSnapshots.Count);
        Assert.Equal(JsonSerializer.Serialize(allSnapshots.Cast<object>().Single(row =>
                (int)row.GetType().GetProperty("Id")!.GetValue(row)! == bank.Id)),
            JsonSerializer.Serialize(selectedSnapshots[0]));
        output.WriteLine($"Selected snapshot: {selectedSnapshots.Count} account vs {allSnapshots.Count} for full report; dashboard commands stay at {smallSummaryCommands}.");

        counter.Reset();
        var period = (Task<decimal>)typeof(FinanceService)
            .GetMethod("AccountPeriodCashMovementAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(finance, [day, day.AddDays(1), bank.Id, CancellationToken.None])!;
        Assert.Equal(180m, await period);
        Assert.Single(counter.Commands);
        Assert.Contains("UNION ALL", counter.Commands[0]);
        output.WriteLine("Account period movement: one SQL aggregate across all existing cash sources.");
    }

    private static Task<FinanceAccountLedgerSliceDto> HistoryWithUnusedBalancesAsync(FinanceAccountService accounts, int id, int skip, int take) =>
        (Task<FinanceAccountLedgerSliceDto>)typeof(FinanceAccountService)
            .GetMethod("QueryTransactionLedgerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(accounts, [id, null, null, null, skip, take, true, false, true, CancellationToken.None])!;

    private static async Task<IList> SnapshotsAsync(FinanceService finance, DateTime day, int? accountId)
    {
        var method = typeof(FinanceService).GetMethod("AccountSnapshotsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)method.Invoke(finance, [null, day, null, CancellationToken.None, accountId])!;
        await task;
        return (IList)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public void Reset() => Commands.Clear();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestDatabase(string masterConnection, string databaseName, string connectionString) : IAsyncDisposable
    {
        private const string Prefix = "DamsAccountPerformanceTests_";
        public string ConnectionString { get; } = connectionString;
        public static async Task<TestDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")
                ?? throw new InvalidOperationException("DAMS_SQLSERVER_TEST_CONNECTION is required.");
            var name = $"{Prefix}{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var test = new SqlConnectionStringBuilder(configured) { InitialCatalog = name };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync();
            return new TestDatabase(master.ConnectionString, name, test.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            if (!databaseName.StartsWith(Prefix, StringComparison.Ordinal) || databaseName.Length != Prefix.Length + 32)
                throw new InvalidOperationException("Refusing to drop an unexpected SQL test database.");
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(masterConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
