using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LeadNotifications is dropped at the end of this migration, after its rows have
            // been copied into the central store — see MigrateLeadNotifications below.

            migrationBuilder.CreateTable(
                name: "EmailSuppressions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClearedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSuppressions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationAuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: false),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActionText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ActionUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    AudienceType = table.Column<int>(type: "int", nullable: false),
                    AudienceJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProcessingStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PushEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InAppEnabled = table.Column<bool>(type: "bit", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PushEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    DelayMinutes = table.Column<int>(type: "int", nullable: false),
                    ReminderLeadDays = table.Column<int>(type: "int", nullable: false),
                    RemindOnDueDate = table.Column<bool>(type: "bit", nullable: false),
                    RepeatWhenOverdue = table.Column<bool>(type: "bit", nullable: false),
                    EscalateToSupervisors = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsSecret = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ActionText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ActionUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Footer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IconUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BadgeUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    P256dh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceLabel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivationReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    DeepLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    DedupKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NotificationJobId = table.Column<int>(type: "int", nullable: true),
                    IsEscalation = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_NotificationJobs_NotificationJobId",
                        column: x => x.NotificationJobId,
                        principalTable: "NotificationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Target = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsPermanentFailure = table.Column<bool>(type: "bit", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSuppressions_Email",
                table: "EmailSuppressions",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAuditEntries_Area_OccurredAt",
                table: "NotificationAuditEntries",
                columns: new[] { "Area", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAuditEntries_OccurredAt",
                table: "NotificationAuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_LockedUntil",
                table: "NotificationDeliveries",
                column: "LockedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationId_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_AvailableAt",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationJobs_RequestKey",
                table: "NotificationJobs",
                column: "RequestKey",
                unique: true,
                filter: "[RequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationJobs_Status_ScheduledAt",
                table: "NotificationJobs",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_Category",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_Type",
                table: "NotificationRules",
                column: "Type",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DedupKey",
                table: "Notifications",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EntityType_EntityId",
                table: "Notifications",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_NotificationJobId",
                table: "Notifications",
                column: "NotificationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_Category_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "Category", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationSettings_Key",
                table: "NotificationSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Type_Channel",
                table: "NotificationTemplates",
                columns: new[] { "Type", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId_IsActive",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.Sql(@"
INSERT INTO [dbo].[NotificationSettings] ([Key], [Value], [IsSecret], [UpdatedAt], [UpdatedByUserId])
VALUES (N'system.platformActivatedAt', CONVERT(nvarchar(33), SYSUTCDATETIME(), 127), 0, SYSUTCDATETIME(), NULL);
");

            MigrateLeadNotifications(migrationBuilder);

            migrationBuilder.DropTable(
                name: "LeadNotifications");
        }

        /// <summary>
        /// Moves the existing lead alerts into the central store so nobody loses history, and
        /// so a repeating alert that was already raised is not raised a second time after the
        /// upgrade.
        ///
        /// Two details matter. The read/unread state travels with the row, so an inbox that
        /// was cleared before the upgrade stays cleared. And the dedup key is rewritten for
        /// the alert types whose scanners use an enum-name prefix and whose names changed.
        /// SiteVisitToday deliberately keeps its old prefix because the current lead scanner
        /// still emits that key; rewriting it would create a duplicate after the upgrade.
        /// </summary>
        private static void MigrateLeadNotifications(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[LeadNotifications]', N'U') IS NULL
    RETURN;

;WITH Mapped AS (
    SELECT
        n.[Id],
        n.[LeadId],
        n.[RecipientUserId],
        n.[Title],
        n.[Body],
        n.[IsRead],
        n.[ReadAt],
        n.[IsEscalation],
        n.[CreatedAt],
        -- Old LeadNotificationType -> central NotificationType.
        CASE n.[Type]
            WHEN 0  THEN 30   -- NewLeadReceived        -> LeadCreated
            WHEN 1  THEN 31   -- LeadAssigned
            WHEN 2  THEN 32   -- LeadReassigned
            WHEN 3  THEN 37   -- FirstContactDue
            WHEN 4  THEN 38   -- FirstContactOverdue
            WHEN 5  THEN 41   -- FollowUpDue
            WHEN 6  THEN 42   -- FollowUpOverdue
            WHEN 7  THEN 36   -- LeadInactive
            WHEN 8  THEN 52   -- SiteVisitToday         -> SiteVisitReminder
            WHEN 9  THEN 53   -- SiteVisitMissed
            WHEN 10 THEN 33   -- StageChanged           -> LeadStageChanged
            WHEN 11 THEN 110  -- ManagerAttentionRequired
            WHEN 12 THEN 34   -- LeadConverted
            WHEN 13 THEN 35   -- LeadClosed
            WHEN 14 THEN 60   -- MentionedInComment     -> UserMentioned
            -- TaskAssigned covered both follow-ups and site visits; the dedup key's suffix
            -- is what said which, so it is what splits them now.
            WHEN 15 THEN CASE WHEN n.[DedupKey] LIKE '%:visit:%' THEN 50 ELSE 40 END
            ELSE 90           -- anything unknown lands as an announcement rather than vanishing
        END AS [NewType],
        -- The scan rebuilds its dedup key from the type name, so renamed types need their
        -- historic keys rewritten to match.
        CASE
            WHEN n.[DedupKey] LIKE 'MentionedInComment:%'  THEN 'UserMentioned:'      + SUBSTRING(n.[DedupKey], 20, 200)
            WHEN n.[DedupKey] LIKE 'NewLeadReceived:%'     THEN 'LeadCreated:'        + SUBSTRING(n.[DedupKey], 17, 200)
            WHEN n.[DedupKey] LIKE 'StageChanged:%'        THEN 'LeadStageChanged:'   + SUBSTRING(n.[DedupKey], 14, 200)
            WHEN n.[DedupKey] LIKE 'TaskAssigned:%:visit:%' THEN 'SiteVisitScheduled:' + SUBSTRING(n.[DedupKey], 14, 200)
            WHEN n.[DedupKey] LIKE 'TaskAssigned:%'        THEN 'FollowUpAssigned:'   + SUBSTRING(n.[DedupKey], 14, 200)
            ELSE n.[DedupKey]
        END AS [NewDedupKey]
    FROM [dbo].[LeadNotifications] n
    -- A notification whose recipient or lead has since been removed has nowhere to live.
    INNER JOIN [dbo].[Users] u ON u.[UserId] = n.[RecipientUserId]
    INNER JOIN [dbo].[Leads] l ON l.[Id] = n.[LeadId]
), Ranked AS (
    SELECT m.*, ROW_NUMBER() OVER (PARTITION BY m.[NewDedupKey] ORDER BY m.[Id]) AS [rn]
    FROM Mapped m
)
INSERT INTO [dbo].[Notifications]
    ([Category], [Type], [Priority], [Module], [Title], [Message], [EntityType], [EntityId],
     [DeepLink], [RecipientUserId], [RecipientEmail], [RecipientName], [CreatedByUserId],
     [CreatedAt], [ExpiresAt], [IsRead], [SeenAt], [ReadAt], [IsArchived], [ArchivedAt],
     [Channels], [DedupKey], [DataJson], [NotificationJobId], [IsEscalation])
SELECT
    CASE
        WHEN m.[NewType] IN (37, 38, 40, 41, 42) THEN 4    -- FollowUps
        WHEN m.[NewType] IN (50, 51, 52)         THEN 5    -- SiteVisits
        WHEN m.[NewType] = 70                    THEN 7    -- EmployeeTasks
        WHEN m.[NewType] = 60                    THEN 6    -- Mentions
        WHEN m.[NewType] IN (43, 53, 110)        THEN 11   -- ManagerEscalations
        ELSE 3                                             -- LeadAssignments
    END,
    m.[NewType],
    CASE WHEN m.[IsEscalation] = 1 THEN 2 ELSE 1 END,      -- High for escalations, else Normal
    1,                                                     -- Module: Leads
    m.[Title],
    ISNULL(m.[Body], N''),
    1,                                                     -- EntityType: Lead
    m.[LeadId],
    N'/crm/leads/' + CAST(m.[LeadId] AS nvarchar(20)),
    m.[RecipientUserId],
    NULL,
    NULL,
    NULL,
    m.[CreatedAt],
    NULL,
    m.[IsRead],
    m.[ReadAt],
    m.[ReadAt],
    0,
    NULL,
    1,                                                     -- Channels: InApp only, as before
    m.[NewDedupKey],
    NULL,
    NULL,
    m.[IsEscalation]
FROM Ranked m
-- Defensive: a rewritten key could collide with one already inserted above.
WHERE m.[rn] = 1
  AND NOT EXISTS (SELECT 1 FROM [dbo].[Notifications] x WHERE x.[DedupKey] = m.[NewDedupKey]);

-- Every migrated alert was already in the recipient's inbox, so its in-app delivery is
-- recorded as sent rather than being queued and re-announced.
INSERT INTO [dbo].[NotificationDeliveries]
    ([NotificationId], [Channel], [Status], [AvailableAt], [AttemptCount], [LastAttemptAt],
     [ProcessingStartedAt], [SentAt], [DeliveredAt], [FailedAt], [Target], [ProviderReference],
     [FailureReason], [IsPermanentFailure], [LockedUntil], [LockedBy])
SELECT n.[Id], 1, 3, n.[CreatedAt], 1, n.[CreatedAt], n.[CreatedAt], n.[CreatedAt], NULL, NULL,
       N'user:' + CAST(n.[RecipientUserId] AS nvarchar(20)), NULL, NULL, 0, NULL, NULL
FROM [dbo].[Notifications] n
WHERE n.[Module] = 1
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[NotificationDeliveries] d
      WHERE d.[NotificationId] = n.[Id] AND d.[Channel] = 1);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the legacy table and its compatible rows before dropping the central
            // store. A rollback must not erase the lead inbox that existed before this
            // migration was applied.
            migrationBuilder.CreateTable(
                name: "LeadNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DedupKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsEscalation = table.Column<bool>(type: "bit", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeadNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.Sql(@"
SET IDENTITY_INSERT [dbo].[LeadNotifications] ON;
;WITH Candidates AS (
    SELECT n.[Id], n.[EntityId] AS [LeadId], n.[RecipientUserId],
           LEFT(n.[Message], 1000) AS [Body], n.[CreatedAt],
           CASE
               WHEN n.[DedupKey] LIKE 'LeadCreated:%'       THEN 'NewLeadReceived:' + SUBSTRING(n.[DedupKey], 13, 200)
               WHEN n.[DedupKey] LIKE 'LeadStageChanged:%' THEN 'StageChanged:'    + SUBSTRING(n.[DedupKey], 18, 200)
               ELSE n.[DedupKey]
           END AS [OldDedupKey],
           n.[IsEscalation], n.[IsRead], n.[ReadAt], n.[Title],
           CASE n.[Type]
               WHEN 30 THEN 0 WHEN 31 THEN 1 WHEN 32 THEN 2 WHEN 37 THEN 3
               WHEN 38 THEN 4 WHEN 41 THEN 5 WHEN 42 THEN 6 WHEN 36 THEN 7
               WHEN 52 THEN 8 WHEN 53 THEN 9 WHEN 33 THEN 10 WHEN 110 THEN 11
               WHEN 34 THEN 12 WHEN 35 THEN 13 WHEN 60 THEN 14
               WHEN 40 THEN 15 WHEN 50 THEN 15 ELSE 11
           END AS [OldType]
    FROM [dbo].[Notifications] n
    INNER JOIN [dbo].[Leads] l ON l.[Id] = n.[EntityId]
    INNER JOIN [dbo].[Users] u ON u.[UserId] = n.[RecipientUserId]
    WHERE n.[Module] = 1 AND n.[EntityType] = 1
      AND n.[EntityId] IS NOT NULL AND n.[RecipientUserId] IS NOT NULL
), Ranked AS (
    SELECT c.*, ROW_NUMBER() OVER (PARTITION BY c.[OldDedupKey] ORDER BY c.[Id]) AS [rn]
    FROM Candidates c
)
INSERT INTO [dbo].[LeadNotifications]
    ([Id], [LeadId], [RecipientUserId], [Body], [CreatedAt], [DedupKey],
     [IsEscalation], [IsRead], [ReadAt], [Title], [Type])
SELECT [Id], [LeadId], [RecipientUserId], [Body], [CreatedAt], [OldDedupKey],
       [IsEscalation], [IsRead], [ReadAt], [Title], [OldType]
FROM Ranked
WHERE [rn] = 1;
SET IDENTITY_INSERT [dbo].[LeadNotifications] OFF;
");

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_DedupKey",
                table: "LeadNotifications",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_LeadId",
                table: "LeadNotifications",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadNotifications_RecipientUserId_IsRead_CreatedAt",
                table: "LeadNotifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAt" });

            migrationBuilder.DropTable(
                name: "EmailSuppressions");

            migrationBuilder.DropTable(
                name: "NotificationAuditEntries");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "NotificationRules");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "NotificationJobs");

        }
    }
}
