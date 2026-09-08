using System.Data.Common;
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

namespace DAMS.Application.Tests;

/// <summary>
/// Opening one account's Details used to run the entire Trial Balance first — every source in the
/// system aggregated for every account — and then read one row out of the result. This counts the
/// commands the production provider actually issues, because the cost being removed is a query
/// fan-out and nothing about the returned figures shows it.
/// <para>
/// The assertions are relative rather than a wall-clock number or a fixed count: Details must cost
/// less than the whole report plus its ledger (which is what it cost by construction before), and
/// the row calculation on its own must cost less than the whole report.
/// </para>
/// </summary>
public sealed class TrialBalanceDetailsSqlTests
{
    [SqlServerFact]
    public async Task Details_ForOneAccount_NoLongerRunsTheWholeTrialBalance()
    {
        await using var database = await SqlTrialBalanceDatabase.CreateAsync();
        var setupOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        int bankId;
        var day = new DateTime(2026, 8, 20);
        await using (var setup = new AppDbContext(setupOptions))
        {
            await setup.Database.MigrateAsync();
            var bank = new FinanceAccount
            {
                Name = "Collection Bank", LedgerCode = "BANK-1", AccountHolderName = "DAMS",
                Type = FinanceAccountType.Bank, OpeningBalance = 250_000m, IsActive = true
            };
            setup.FinanceAccounts.Add(bank);
            await setup.SaveChangesAsync();
            bankId = bank.Id;
        }

        var counter = new CommandCounter();
        var measuredOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(counter)
            .Options;
        await using var context = new AppDbContext(measuredOptions);
        var accounts = new FinanceAccountService(context);
        var finance = new FinanceService(context, new NoopStorage(), accounts,
            new WhtService(context, accounts), NullLogger<FinanceService>.Instance);
        var accountKey = $"A:{bankId}";

        // Warm the query cache first, so the counts compare work rather than first-use compilation.
        await finance.GetTrialBalanceAsync(null, day, 0);
        await accounts.GetTransactionLedgerSliceAsync(bankId, null, day, day, 0, int.MaxValue);
        await finance.GetTrialBalanceDetailsAsync(accountKey, null, day, day);

        counter.Reset();
        await finance.GetTrialBalanceAsync(null, day, 0);
        var report = counter.Count;

        counter.Reset();
        await accounts.GetTransactionLedgerSliceAsync(bankId, null, day, day, 0, int.MaxValue);
        var ledger = counter.Count;

        counter.Reset();
        await finance.GetTrialBalanceDetailsAsync(accountKey, null, day, day);
        var details = counter.Count;

        Assert.True(report > 1, $"The report should issue more than one command; it issued {report}.");
        Assert.True(details < report + ledger,
            $"Details issued {details} commands; the old path was the report ({report}) plus the ledger ({ledger}).");
        Assert.True(details - ledger < report,
            $"The targeted row issued {details - ledger} commands; the whole report issues {report}.");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoopStorage : IFinanceAttachmentStorage
    {
        public Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");
        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SqlTrialBalanceDatabase : IAsyncDisposable
    {
        private const string Prefix = "DamsTrialBalanceDetailsTests_";
        private readonly string _masterConnection;
        private readonly string _databaseName;

        public string ConnectionString { get; }

        private SqlTrialBalanceDatabase(string masterConnection, string databaseName, string connectionString)
        {
            _masterConnection = masterConnection;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public static async Task<SqlTrialBalanceDatabase> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("DAMS_SQLSERVER_TEST_CONNECTION")
                ?? throw new InvalidOperationException("DAMS_SQLSERVER_TEST_CONNECTION is required.");
            var databaseName = $"{Prefix}{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
            var test = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };

            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
            await command.ExecuteNonQueryAsync();
            return new SqlTrialBalanceDatabase(master.ConnectionString, databaseName, test.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_databaseName.StartsWith(Prefix, StringComparison.Ordinal)
                || _databaseName.Length != Prefix.Length + 32)
                throw new InvalidOperationException("Refusing to drop an unexpected SQL test database.");

            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                + $"DROP DATABASE [{_databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
