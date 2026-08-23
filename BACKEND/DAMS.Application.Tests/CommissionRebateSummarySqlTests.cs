using System.Data.Common;
using DAMS.Application.Interfaces;
using DAMS.Application.Services;
using DAMS.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DAMS.Application.Tests;

public sealed class CommissionRebateSummarySqlTests
{
    [SqlServerFact]
    public async Task Summary_UsesAtMostSevenCommandsOnTheProductionProvider()
    {
        await using var database = await SqlSummaryDatabase.CreateAsync();
        var setupOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using (var setup = new AppDbContext(setupOptions))
            await setup.Database.MigrateAsync();

        var counter = new CommandCounter();
        var measuredOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(counter)
            .Options;
        await using var context = new AppDbContext(measuredOptions);
        var service = new CommissionRebateService(context, new FinanceAccountService(context), new NullStorage());

        counter.Reset();
        var summary = await service.GetSummaryAsync();

        Assert.InRange(counter.Count, 1, 7);
        Assert.Equal(0m, summary.AccruedCommission);
        Assert.Equal(0m, summary.PayableCommission);
        Assert.Equal(0, summary.PendingRecords);
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

    private sealed class NullStorage : IFinancialEvidenceStorage
    {
        public Task<string> SaveAsync(Stream content, string extension,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"{Guid.NewGuid():N}{extension}");

        public Task<Stream?> OpenReadAsync(string storedFileName,
            CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string storedFileName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SqlSummaryDatabase : IAsyncDisposable
    {
        private const string Prefix = "DamsCommissionSummaryTests_";
        private readonly string _masterConnection;
        private readonly string _databaseName;

        public string ConnectionString { get; }

        private SqlSummaryDatabase(string masterConnection, string databaseName, string connectionString)
        {
            _masterConnection = masterConnection;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public static async Task<SqlSummaryDatabase> CreateAsync()
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
            return new SqlSummaryDatabase(master.ConnectionString, databaseName, test.ConnectionString);
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
