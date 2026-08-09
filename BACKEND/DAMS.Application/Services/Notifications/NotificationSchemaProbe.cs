using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAMS.Application.Services.Notifications
{
    internal static class NotificationSchemaProbe
    {
        public static async Task<bool> ExistsAsync(
            AppDbContext context,
            CancellationToken cancellationToken,
            bool includePush = false,
            bool includeJobs = false)
        {
            // Unit/integration harnesses use EF's in-memory provider, where EnsureCreated
            // already establishes the model and relational metadata APIs do not exist.
            if (!context.Database.IsRelational())
                return true;

            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                // A caller may already have an EF transaction open on this connection. SQL
                // Server rejects a command on a connection with a pending local transaction
                // unless the command is enlisted in it.
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = $"""
                    SELECT CASE WHEN
                        OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationDeliveries]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationRules]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationPreferences]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationSettings]', N'U') IS NOT NULL
                        {(includePush ? "AND OBJECT_ID(N'[dbo].[PushSubscriptions]', N'U') IS NOT NULL" : string.Empty)}
                        {(includeJobs ? "AND OBJECT_ID(N'[dbo].[NotificationJobs]', N'U') IS NOT NULL" : string.Empty)}
                    THEN 1 ELSE 0 END
                    """;

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(result) == 1;
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
