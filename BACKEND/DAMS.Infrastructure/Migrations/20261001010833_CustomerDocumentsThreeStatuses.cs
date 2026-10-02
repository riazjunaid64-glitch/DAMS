using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDocumentsThreeStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "CustomerDocumentVersions");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "CustomerDocumentVersions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CustomerDocumentVersions");

            migrationBuilder.DropColumn(
                name: "ReviewedByName",
                table: "CustomerDocumentVersions");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "CustomerDocumentVersions");

            migrationBuilder.DropColumn(
                name: "AllowedFileTypes",
                table: "CustomerDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "CustomerDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "MaxFileSizeBytes",
                table: "CustomerDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "AllowedFileTypes",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "DefaultDueDays",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "MaxFileSizeBytes",
                table: "CustomerDocumentCategories");

            migrationBuilder.RenameColumn(
                name: "PostponedUntil",
                table: "CustomerDocumentRequirements",
                newName: "NotNeededAt");

            migrationBuilder.AddColumn<string>(
                name: "NotNeededByName",
                table: "CustomerDocumentRequirements",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotNeededByUserId",
                table: "CustomerDocumentRequirements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotNeededReason",
                table: "CustomerDocumentRequirements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Old statuses: 0 Missing, 1 Requested, 2 Received, 3 UnderReview, 4 Approved, 5 Rejected,
            // 6 ReplacementRequired, 7 Postponed, 8 Waived, 9 NotApplicable, 10 Expired.
            // New: 0 Needed, 1 Uploaded, 2 Not needed.
            //
            // 1. The old postponed date was renamed to "not needed at": clear it for every row that is
            //    not going to be Not needed.
            migrationBuilder.Sql(@"
UPDATE CustomerDocumentRequirements SET NotNeededAt = NULL WHERE Status NOT IN (8, 9);");

            // 2. Waived and Not applicable become Not needed and keep what was said and by whom: the
            //    reason, person and time come from the latest Waived / Not applicable audit row
            //    (actions 9 and 10), falling back to the row's last change time.
            migrationBuilder.Sql(@"
UPDATE r
SET NotNeededReason = LEFT(a.Notes, 500),
    NotNeededByUserId = a.PerformedByUserId,
    NotNeededByName = a.PerformedByName,
    NotNeededAt = COALESCE(a.OccurredAt, r.UpdatedAt)
FROM CustomerDocumentRequirements r
OUTER APPLY (
    SELECT TOP 1 Notes, PerformedByUserId, PerformedByName, OccurredAt
    FROM CustomerDocumentAuditEntries
    WHERE RequirementId = r.Id AND Action IN (9, 10)
    ORDER BY Id DESC) a
WHERE r.Status IN (8, 9);");

            // 3. Remap the statuses in one statement per table so no row is remapped twice. The audit
            //    rows keep their Action (Approved, Rejected, Postponed ...) and Notes, which say what
            //    happened; only the status numbers move to the new meaning, so no old number is read
            //    as a different new status.
            migrationBuilder.Sql(@"
UPDATE CustomerDocumentRequirements
SET Status = CASE
    WHEN Status IN (2, 3, 4) THEN 1
    WHEN Status IN (8, 9) THEN 2
    ELSE 0 END;");
            migrationBuilder.Sql(@"
UPDATE CustomerDocumentAuditEntries
SET PreviousStatus = CASE
        WHEN PreviousStatus IS NULL THEN NULL
        WHEN PreviousStatus IN (2, 3, 4) THEN 1
        WHEN PreviousStatus IN (8, 9) THEN 2
        ELSE 0 END,
    NewStatus = CASE
        WHEN NewStatus IS NULL THEN NULL
        WHEN NewStatus IN (2, 3, 4) THEN 1
        WHEN NewStatus IN (8, 9) THEN 2
        ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE CustomerDocumentRequirements
SET Status = CASE WHEN Status = 1 THEN 4 WHEN Status = 2 THEN 8 ELSE 0 END;");
            migrationBuilder.Sql(@"
UPDATE CustomerDocumentAuditEntries
SET PreviousStatus = CASE WHEN PreviousStatus IS NULL THEN NULL WHEN PreviousStatus = 1 THEN 4 WHEN PreviousStatus = 2 THEN 8 ELSE 0 END,
    NewStatus = CASE WHEN NewStatus IS NULL THEN NULL WHEN NewStatus = 1 THEN 4 WHEN NewStatus = 2 THEN 8 ELSE 0 END;");

            migrationBuilder.DropColumn(
                name: "NotNeededByName",
                table: "CustomerDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "NotNeededByUserId",
                table: "CustomerDocumentRequirements");

            migrationBuilder.DropColumn(
                name: "NotNeededReason",
                table: "CustomerDocumentRequirements");

            migrationBuilder.RenameColumn(
                name: "NotNeededAt",
                table: "CustomerDocumentRequirements",
                newName: "PostponedUntil");

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "CustomerDocumentVersions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "CustomerDocumentVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CustomerDocumentVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByName",
                table: "CustomerDocumentVersions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedByUserId",
                table: "CustomerDocumentVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedFileTypes",
                table: "CustomerDocumentRequirements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "CustomerDocumentRequirements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxFileSizeBytes",
                table: "CustomerDocumentRequirements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "AllowedFileTypes",
                table: "CustomerDocumentCategories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DefaultDueDays",
                table: "CustomerDocumentCategories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxFileSizeBytes",
                table: "CustomerDocumentCategories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });

            migrationBuilder.UpdateData(
                table: "CustomerDocumentCategories",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "AllowedFileTypes", "DefaultDueDays", "MaxFileSizeBytes" },
                values: new object[] { ".pdf,.jpg,.jpeg,.png", null, 10485760L });
        }
    }
}
