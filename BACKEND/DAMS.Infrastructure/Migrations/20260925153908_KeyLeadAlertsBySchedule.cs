using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Lead alerts are keyed by the schedule they were raised for.
    /// <para>
    /// Rescheduling a follow-up or site visit keeps the same row, and the alert dedup keys held
    /// only the row id, so an alert sent for the old time silently blocked every alert for the
    /// new one. <c>LeadAlertService</c> now appends the row's reschedule count to those keys;
    /// follow-ups gain the <c>RescheduleCount</c> that site visits already carry.
    /// </para>
    /// <para>
    /// Reminders already sent carry the old key, which the new code no longer recognises, so
    /// without the data step below every open follow-up and today's visits would be reminded a
    /// second time on the first scan after release. Only reminders raised for the row's CURRENT
    /// schedule are rekeyed: while a follow-up is Pending (or a visit Scheduled / Rescheduled)
    /// the only write that sets UpdatedAt is a reschedule, so anything created on or after
    /// COALESCE(UpdatedAt, CreatedAt) belongs to the current time, and a reminder for an earlier
    /// time keeps its old key and does not suppress the new one. Existing rows start at count 0.
    /// The NOT EXISTS guard keeps the unique DedupKey index intact if a scan with the new code
    /// has already written the new key. Missed escalations need no rekey: a missed row is not
    /// scanned again until it is rescheduled, which moves it to a new count anyway.
    /// </para>
    /// <para>
    /// Deploy order: the new code maps the new column, so every read of a follow-up fails until
    /// this has run; and once it has run, the OLD scan no longer recognises the rekeyed reminders
    /// and would send them again. Apply it immediately before the new code goes live (inside one
    /// scan interval), or pause the scan for the release with LeadAlerts:ScanIntervalMinutes = 0.
    /// </para>
    /// <para>
    /// Down drops the column but leaves keys as they are: shortening them again could collide
    /// under the unique index, and the only cost of not doing so is at most one repeat reminder
    /// per open item from the older code.
    /// </para>
    /// </summary>
    public partial class KeyLeadAlertsBySchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RescheduleCount",
                table: "LeadFollowUps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Follow-up due / overdue reminders: FollowUpDue:{id}:{user} -> FollowUpDue:{id}:{user}:0.
            // Status 0 = Pending; EntityType 1 = Lead; Type 41 = FollowUpDue, 42 = FollowUpOverdue.
            // The EntityType / Type filters let the (EntityType, EntityId) index narrow the scan.
            migrationBuilder.Sql("""
                UPDATE n
                SET n.[DedupKey] = n.[DedupKey] + N':0'
                FROM [Notifications] n
                JOIN [LeadFollowUps] f
                    ON n.[EntityType] = 1 AND n.[EntityId] = f.[LeadId]
                   AND n.[Type] IN (41, 42)
                   AND n.[DedupKey] IN (CONCAT(N'FollowUpDue:', f.[Id], N':', n.[RecipientUserId]),
                                        CONCAT(N'FollowUpOverdue:', f.[Id], N':', n.[RecipientUserId]))
                WHERE f.[Status] = 0
                  AND n.[CreatedAt] >= COALESCE(f.[UpdatedAt], f.[CreatedAt])
                  AND NOT EXISTS (SELECT 1 FROM [Notifications] x WHERE x.[DedupKey] = n.[DedupKey] + N':0');
                """);

            // Same-day visit reminders: SiteVisitToday:{id}:{user}:{yyyy-MM-dd} -> ...:{RescheduleCount}.
            // Status 0 = Scheduled, 1 = Rescheduled; EntityType 1 = Lead; Type 52 = SiteVisitReminder.
            migrationBuilder.Sql("""
                UPDATE n
                SET n.[DedupKey] = CONCAT(n.[DedupKey], N':', v.[RescheduleCount])
                FROM [Notifications] n
                JOIN [LeadSiteVisits] v
                    ON n.[EntityType] = 1 AND n.[EntityId] = v.[LeadId]
                   AND n.[Type] = 52
                   AND n.[DedupKey] LIKE CONCAT(N'SiteVisitToday:', v.[Id], N':', n.[RecipientUserId], N':____-__-__')
                WHERE v.[Status] IN (0, 1)
                  AND n.[CreatedAt] >= COALESCE(v.[UpdatedAt], v.[CreatedAt])
                  AND NOT EXISTS (SELECT 1 FROM [Notifications] x
                                  WHERE x.[DedupKey] = CONCAT(n.[DedupKey], N':', v.[RescheduleCount]));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RescheduleCount",
                table: "LeadFollowUps");
        }
    }
}
