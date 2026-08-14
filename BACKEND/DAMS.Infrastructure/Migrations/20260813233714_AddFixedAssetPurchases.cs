using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments");

            migrationBuilder.AddColumn<int>(
                name: "AssetPurchaseId",
                table: "FinanceAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    AssetAccountId = table.Column<int>(type: "int", nullable: false),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WhtApplied = table.Column<bool>(type: "bit", nullable: false),
                    WhtRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    WhtAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WhtRateOverridden = table.Column<bool>(type: "bit", nullable: false),
                    WhtOverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WhtTaxSection = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VendorFilerStatusAtEntry = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetPurchases_ExpenseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetPurchases_FinanceAccounts_AssetAccountId",
                        column: x => x.AssetAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetPurchases_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetPurchases_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetPurchases_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAttachments_AssetPurchaseId",
                table: "FinanceAttachments",
                column: "AssetPurchaseId",
                unique: true,
                filter: "[AssetPurchaseId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments",
                sql: "(CASE WHEN [ManualRevenueId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AssetPurchaseId] IS NULL THEN 0 ELSE 1 END) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_AssetAccountId_Date",
                table: "AssetPurchases",
                columns: new[] { "AssetAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_CategoryId",
                table: "AssetPurchases",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_Date",
                table: "AssetPurchases",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_FinanceAccountId",
                table: "AssetPurchases",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_ProjectId",
                table: "AssetPurchases",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetPurchases_VendorId_Date",
                table: "AssetPurchases",
                columns: new[] { "VendorId", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceAttachments_AssetPurchases_AssetPurchaseId",
                table: "FinanceAttachments",
                column: "AssetPurchaseId",
                principalTable: "AssetPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinanceAttachments_AssetPurchases_AssetPurchaseId",
                table: "FinanceAttachments");

            migrationBuilder.DropTable(
                name: "AssetPurchases");

            migrationBuilder.DropIndex(
                name: "IX_FinanceAttachments_AssetPurchaseId",
                table: "FinanceAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments");

            migrationBuilder.DropColumn(
                name: "AssetPurchaseId",
                table: "FinanceAttachments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceAttachments_ExactlyOneOwner",
                table: "FinanceAttachments",
                sql: "([ManualRevenueId] IS NOT NULL AND [ExpenseId] IS NULL) OR ([ManualRevenueId] IS NULL AND [ExpenseId] IS NOT NULL)");
        }
    }
}
