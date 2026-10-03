using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveGoLiveDateToFinanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GoLiveDate",
                table: "FinanceSettings",
                type: "datetime2",
                nullable: true);

            // The go-live date was the date of the committed opening balance set (the same
            // expression the commission accrual backfill used). A draft that was never committed
            // leaves it empty, which means no posting-date limit, as before.
            migrationBuilder.Sql("""
                UPDATE [FinanceSettings] SET [GoLiveDate] = (
                    SELECT MAX([AsAtDate]) FROM [OpeningBalanceSets] WHERE [CommittedAt] IS NOT NULL)
                WHERE [Id] = 1;
                """);

            // The old workflow's history moves into the shared correction trail before its tables go.
            migrationBuilder.Sql("""
                INSERT INTO [FinanceRecordAudits] ([RecordType],[RecordId],[Action],[Changes],[ActorUserId],[OccurredAt])
                SELECT 'OpeningBalanceSet', a.[OpeningBalanceSetId], LEFT(a.[Action], 20),
                    ISNULL((SELECT a.[Note] AS [note] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}'),
                    a.[UserId], a.[OccurredAt]
                FROM [OpeningBalanceAuditEntries] a;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [FinanceRecordAudits] ([RecordType],[RecordId],[Action],[Changes],[ActorUserId],[OccurredAt])
                SELECT 'FinanceAccount', e.[FinanceAccountId], 'OpeningNote',
                    (SELECT s.[AsAtDate] AS [asAt], e.[DebitAmount] AS [debit], e.[CreditAmount] AS [credit],
                        e.[Note] AS [note] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                    s.[CommittedByUserId], ISNULL(s.[CommittedAt], SYSUTCDATETIME())
                FROM [OpeningBalanceEntries] e
                JOIN [OpeningBalanceSets] s ON s.[Id] = e.[OpeningBalanceSetId]
                WHERE e.[Note] IS NOT NULL OR e.[DebitAmount] <> 0 OR e.[CreditAmount] <> 0;
                """);

            migrationBuilder.DropTable(
                name: "OpeningBalanceAuditEntries");

            migrationBuilder.DropTable(
                name: "OpeningBalanceEntries");

            migrationBuilder.DropTable(
                name: "OpeningBalanceSets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoLiveDate",
                table: "FinanceSettings");

            migrationBuilder.CreateTable(
                name: "OpeningBalanceSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsAtDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommittedByUserId = table.Column<int>(type: "int", nullable: true),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceSets", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceSets_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceAuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OpeningBalanceSetId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceAuditEntries_OpeningBalanceSets_OpeningBalanceSetId",
                        column: x => x.OpeningBalanceSetId,
                        principalTable: "OpeningBalanceSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    OpeningBalanceSetId = table.Column<int>(type: "int", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceEntries", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceEntry_NonNegative", "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
                    table.CheckConstraint("CK_OpeningBalanceEntry_OneSide", "[DebitAmount] = 0 OR [CreditAmount] = 0");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceEntries_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceEntries_OpeningBalanceSets_OpeningBalanceSetId",
                        column: x => x.OpeningBalanceSetId,
                        principalTable: "OpeningBalanceSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceAuditEntries_OpeningBalanceSetId_OccurredAt",
                table: "OpeningBalanceAuditEntries",
                columns: new[] { "OpeningBalanceSetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceEntries_FinanceAccountId",
                table: "OpeningBalanceEntries",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceEntries_OpeningBalanceSetId_FinanceAccountId",
                table: "OpeningBalanceEntries",
                columns: new[] { "OpeningBalanceSetId", "FinanceAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceSets_AsAtDate",
                table: "OpeningBalanceSets",
                column: "AsAtDate",
                unique: true);
        }
    }
}
