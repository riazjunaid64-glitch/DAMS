using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadCommunicationConnected : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Connected",
                table: "LeadCommunications",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // Mark old contact attempts (failed calls, etc.) as not connected.
            // ContactAttempt = 5 (from LeadActivityType enum)
            migrationBuilder.Sql(@"
UPDATE c SET c.Connected = 0
FROM LeadCommunications c
JOIN LeadActivities a ON a.CommunicationId = c.Id
WHERE a.Type = 5;");

            // KAN-24: recompute next action for open leads (Won=9, Lost=10, Dormant=11 excluded).
            // This brings existing production leads into compliance with the new rules.
            migrationBuilder.Sql(@"
UPDATE l
SET l.NextActionAt      = n.At,
    l.NextActionSummary = CASE WHEN n.Summary IS NOT NULL THEN LEFT(n.Summary, 300) ELSE NULL END,
    l.UpdatedAt         = SYSUTCDATETIME()
FROM Leads l
OUTER APPLY (SELECT MAX(a.OccurredAt) AS ReopenedAt
             FROM LeadActivities a WHERE a.LeadId = l.Id AND a.Type = 32) r
OUTER APPLY (
    SELECT TOP 1 x.At, x.Summary FROM (
        SELECT * FROM (SELECT TOP 1 f.DueAt AS At, f.Title AS Summary
                       FROM LeadFollowUps f
                       WHERE f.LeadId = l.Id AND f.Status IN (0, 3)
                       ORDER BY f.DueAt) fu
        UNION ALL
        SELECT * FROM (SELECT TOP 1 v.ScheduledAt, N'Site visit at ' + v.MeetingLocation
                       FROM LeadSiteVisits v
                       WHERE v.LeadId = l.Id AND v.Status IN (0, 1, 4)
                       ORDER BY v.ScheduledAt) sv
        UNION ALL
        SELECT * FROM (SELECT TOP 1 c.NextActionAt, COALESCE(c.NextAction, c.Summary)
                       FROM LeadCommunications c
                       WHERE c.LeadId = l.Id
                         AND (c.Connected = 1 OR c.NextActionAt IS NOT NULL)
                         AND (r.ReopenedAt IS NULL OR c.OccurredAt >= r.ReopenedAt)
                       ORDER BY c.OccurredAt DESC, c.Id DESC) cm
    ) x
    WHERE x.At IS NOT NULL
    ORDER BY x.At
) n
WHERE l.Stage NOT IN (9, 10, 11);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Connected",
                table: "LeadCommunications");
        }
    }
}
