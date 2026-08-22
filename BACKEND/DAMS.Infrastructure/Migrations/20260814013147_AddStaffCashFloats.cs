using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffCashFloats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffCashTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffFinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    CounterpartyFinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffCashTransfers", x => x.Id);
                    table.CheckConstraint("CK_StaffCashTransfers_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_StaffCashTransfers_DifferentAccounts", "[StaffFinanceAccountId] <> [CounterpartyFinanceAccountId]");
                    table.CheckConstraint("CK_StaffCashTransfers_Type", "[Type] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_StaffCashTransfers_FinanceAccounts_CounterpartyFinanceAccountId",
                        column: x => x.CounterpartyFinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffCashTransfers_FinanceAccounts_StaffFinanceAccountId",
                        column: x => x.StaffFinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_Type_AccountHolderName",
                table: "FinanceAccounts",
                columns: new[] { "Type", "AccountHolderName" },
                unique: true,
                filter: "[Type] = 10");

            migrationBuilder.CreateIndex(
                name: "IX_StaffCashTransfers_CounterpartyFinanceAccountId_Date",
                table: "StaffCashTransfers",
                columns: new[] { "CounterpartyFinanceAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffCashTransfers_StaffFinanceAccountId_Date_CreatedAt",
                table: "StaffCashTransfers",
                columns: new[] { "StaffFinanceAccountId", "Date", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffCashTransfers");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAccounts_Type_AccountHolderName",
                table: "FinanceAccounts");
        }
    }
}
