using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Lets a financial statement be read as at one instant.
    /// <para>
    /// A Balance Sheet needs a dozen queries to build, and under READ COMMITTED each one sees
    /// whatever was committed by the moment it ran. A payment committing between the query that
    /// reads the bank and the query that reads the customer balances lands on one side of the
    /// sheet and not the other, and the report then declares an Imbalance — or the Trial Balance
    /// drill-down refuses to reconcile — over books that are perfectly correct. That false fault is
    /// indistinguishable from a real one, which is precisely what makes the check worth having.
    /// </para>
    /// <para>
    /// Snapshot isolation is the level that fixes it without a cost anyone pays: every read in the
    /// window sees the database as at the instant the window opened, and readers take no locks, so
    /// a long report never blocks a cashier taking money. Serialisable would give the same
    /// consistency by holding range locks across the whole finance schema for the life of the
    /// report — correct, and unusable.
    /// </para>
    /// <para>
    /// Nothing in the schema changes, so there is no model snapshot difference; this is a database
    /// option. READ_COMMITTED_SNAPSHOT is deliberately left alone — every write path here chooses
    /// its own isolation explicitly (see the serialisable guards in WhtService and LoanService), and
    /// changing what READ COMMITTED means underneath them is a separate decision.
    /// </para>
    /// </summary>
    public partial class EnableSnapshotIsolationForReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // suppressTransaction: ALTER DATABASE ... SET cannot run inside a user transaction, and
            // EF wraps a migration in one by default. The statement waits for transactions that are
            // already open before it takes effect, so run it when the system is quiet.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.databases
                           WHERE [name] = DB_NAME() AND [snapshot_isolation_state] <> 1)
                BEGIN
                    DECLARE @sql nvarchar(max) =
                        N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET ALLOW_SNAPSHOT_ISOLATION ON';
                    EXEC sp_executesql @sql;
                END
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reports fall back to READ COMMITTED on their own once this is off — see
            // FinanceService.ReadConsistentlyAsync — so rolling back degrades them rather than
            // breaking them.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.databases
                           WHERE [name] = DB_NAME() AND [snapshot_isolation_state] = 1)
                BEGIN
                    DECLARE @sql nvarchar(max) =
                        N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET ALLOW_SNAPSHOT_ISOLATION OFF';
                    EXEC sp_executesql @sql;
                END
                """,
                suppressTransaction: true);
        }
    }
}
