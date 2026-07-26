using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinanceAccountId",
                table: "ManualRevenues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinanceAccountId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinanceAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AccountHolderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BankOrWalletName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualRevenues_FinanceAccountId",
                table: "ManualRevenues",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_FinanceAccountId",
                table: "Expenses",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_AccountHolderName",
                table: "FinanceAccounts",
                column: "AccountHolderName");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_IsActive_Type",
                table: "FinanceAccounts",
                columns: new[] { "IsActive", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_Name",
                table: "FinanceAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_FinanceAccounts_FinanceAccountId",
                table: "Expenses",
                column: "FinanceAccountId",
                principalTable: "FinanceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ManualRevenues_FinanceAccounts_FinanceAccountId",
                table: "ManualRevenues",
                column: "FinanceAccountId",
                principalTable: "FinanceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_FinanceAccounts_FinanceAccountId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_ManualRevenues_FinanceAccounts_FinanceAccountId",
                table: "ManualRevenues");

            migrationBuilder.DropTable(
                name: "FinanceAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ManualRevenues_FinanceAccountId",
                table: "ManualRevenues");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_FinanceAccountId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "FinanceAccountId",
                table: "ManualRevenues");

            migrationBuilder.DropColumn(
                name: "FinanceAccountId",
                table: "Expenses");
        }
    }
}
