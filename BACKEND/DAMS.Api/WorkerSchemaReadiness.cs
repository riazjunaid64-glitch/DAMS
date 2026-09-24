using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Api
{
    /// <summary>
    /// The startup gate shared by the background workers: wait until the database answers,
    /// then report whether the worker's tables exist.
    ///
    /// Only a real answer of "missing" means the worker should stop. A check that could not
    /// reach the database is retried with a doubling delay, so a database that comes up after
    /// the API does not leave the worker idle until someone restarts the instance.
    /// </summary>
    internal static class WorkerSchemaReadiness
    {
        public static async Task<bool> WaitForTablesAsync(
            IServiceScopeFactory scopeFactory,
            ILogger logger,
            string processing,
            IReadOnlyList<string> tables,
            int startupDelaySeconds,
            int maxRetryDelaySeconds,
            CancellationToken stoppingToken)
        {
            var maxRetryDelay = TimeSpan.FromSeconds(maxRetryDelaySeconds);
            var retryDelay = TimeSpan.FromSeconds(Math.Clamp(startupDelaySeconds, 1, maxRetryDelaySeconds));

            await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);

            for (var failures = 0; ; failures++)
            {
                try
                {
                    var exists = await TablesExistAsync(scopeFactory, tables, stoppingToken);
                    if (exists && failures > 0)
                        logger.LogInformation(
                            "{Processing} started after {Failures} failed database check(s).", processing, failures);
                    else if (exists)
                        logger.LogInformation("{Processing} started.", processing);
                    return exists;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex,
                        "{Processing} is waiting for the database (check {Attempt} failed). " +
                        "Retrying in {DelaySeconds} seconds.", processing, failures + 1, retryDelay.TotalSeconds);
                    await Task.Delay(retryDelay, stoppingToken);
                    retryDelay = retryDelay * 2 < maxRetryDelay ? retryDelay * 2 : maxRetryDelay;
                }
            }
        }

        /// <summary>Throws when the database cannot be asked at all.</summary>
        private static async Task<bool> TablesExistAsync(
            IServiceScopeFactory scopeFactory, IReadOnlyList<string> tables, CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection();

            await db.Database.OpenConnectionAsync(stoppingToken);
            try
            {
                await using var command = connection.CreateCommand();
                // Table names are compile-time constants from the workers, never input.
                command.CommandText =
                    "SELECT CASE WHEN " +
                    string.Join(" AND ", tables.Select(t => $"OBJECT_ID(N'[dbo].[{t}]', N'U') IS NOT NULL")) +
                    " THEN 1 ELSE 0 END";

                var result = await command.ExecuteScalarAsync(stoppingToken);
                return Convert.ToInt32(result) == 1;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
