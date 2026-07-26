using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT CASE WHEN
                        OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationDeliveries]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[dbo].[NotificationRules]', N'U') IS NOT NULL AND
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
