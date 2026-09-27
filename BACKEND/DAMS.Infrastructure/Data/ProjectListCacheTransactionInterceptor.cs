using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DAMS.Infrastructure.Data
{
    /// <summary>
    /// Bumps the project-list cache only after the transaction that changed a unit commits.
    /// In-memory tests have no relational transaction, so SaveChanges bumps immediately.
    /// Registered where the context is configured (Program.cs): the context is pooled, and EF Core
    /// refuses options changes made in OnConfiguring when pooling is on.
    /// </summary>
    public sealed class ProjectListCacheTransactionInterceptor : DbTransactionInterceptor
    {
        public static readonly ProjectListCacheTransactionInterceptor Instance = new();

        private ProjectListCacheTransactionInterceptor()
        {
        }

        // A pooled context keeps its fields between requests, and a transaction disposed without
        // commit or rollback raises neither event below, so a new transaction starts clean.
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            if (eventData.Context is AppDbContext context)
                context.ProjectListCachePending = false;
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            TransactionStarted(connection, eventData, result);
            return ValueTask.FromResult(result);
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (eventData.Context is AppDbContext context && context.ProjectListCachePending)
            {
                context.ProjectListCachePending = false;
                ProjectListCache.Bump();
            }
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            TransactionCommitted(transaction, eventData);
            return Task.CompletedTask;
        }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (eventData.Context is AppDbContext context)
                context.ProjectListCachePending = false;
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            TransactionRolledBack(transaction, eventData);
            return Task.CompletedTask;
        }
    }
}
