using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWithholdingTaxOnExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorFilerStatusAtEntry",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WhtAmount",
                table: "Expenses",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "WhtApplied",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhtOverrideReason",
                table: "Expenses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WhtRate",
                table: "Expenses",
                type: "decimal(9,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "WhtRateOverridden",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhtTaxSection",
                table: "Expenses",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsWhtApplicable = table.Column<bool>(type: "bit", nullable: false),
                    FilerRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    NonFilerRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    AnnualThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxSection = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialYearStartMonth = table.Column<int>(type: "int", nullable: false),
                    WhtRatesConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WhtRatesConfirmedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceSettings", x => x.Id);
                    table.CheckConstraint("CK_FinanceSettings_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ntn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Cnic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FilerStatus = table.Column<int>(type: "int", nullable: false),
                    FilerStatusCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhtDeposits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DepositDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChallanNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhtDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhtDeposits_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "AnnualThreshold", "Code", "CreatedAt", "CreatedByUserId", "Description", "DisplayOrder", "FilerRate", "IsActive", "IsWhtApplicable", "Name", "NonFilerRate", "TaxSection", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, 75000m, "cement", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10, 1.00m, true, true, "Cement", 2.00m, "153(1)(a)", null },
                    { 2, 75000m, "steel_rebar", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 20, 1.00m, true, true, "Steel / Iron / Rebar", 2.00m, "153(1)(a)", null },
                    { 3, 75000m, "sand_aggregate", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 30, 5.00m, true, true, "Sand, Gravel & Aggregate", 10.00m, "153(1)(a)", null },
                    { 4, 75000m, "bricks_blocks", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 40, 5.00m, true, true, "Bricks & Blocks", 10.00m, "153(1)(a)", null },
                    { 5, 75000m, "tiles_marble", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 50, 5.00m, true, true, "Tiles, Marble & Flooring", 10.00m, "153(1)(a)", null },
                    { 6, 75000m, "wood_timber", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 60, 5.00m, true, true, "Wood & Timber", 10.00m, "153(1)(a)", null },
                    { 7, 75000m, "paint_finishing", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 70, 5.00m, true, true, "Paint & Finishing Materials", 10.00m, "153(1)(a)", null },
                    { 8, 75000m, "electrical_materials", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 80, 5.00m, true, true, "Electrical Materials", 10.00m, "153(1)(a)", null },
                    { 9, 75000m, "plumbing_sanitary", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 90, 5.00m, true, true, "Plumbing & Sanitary Materials", 10.00m, "153(1)(a)", null },
                    { 10, 75000m, "glass_aluminium", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 100, 5.00m, true, true, "Glass & Aluminium", 10.00m, "153(1)(a)", null },
                    { 11, 75000m, "hardware_tools", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 110, 5.00m, true, true, "Hardware & Tools", 10.00m, "153(1)(a)", null },
                    { 12, 75000m, "other_materials", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 120, 5.00m, true, true, "Other Construction Materials", 10.00m, "153(1)(a)", null },
                    { 20, 30000m, "contract_company", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 200, 7.50m, true, true, "Construction Contract (Company)", 15.00m, "153(1)(c)", null },
                    { 21, 30000m, "contract_individual", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 210, 8.00m, true, true, "Construction Contract (Individual/AOP)", 16.00m, "153(1)(c)", null },
                    { 22, 30000m, "labour_manpower", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 220, 15.00m, true, true, "Labour & Manpower", 30.00m, "153(1)(b)", null },
                    { 23, 30000m, "architecture_design", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 230, 15.00m, true, true, "Architecture & Design Fee", 30.00m, "153(1)(b)", null },
                    { 24, 30000m, "engineering_consultancy", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 240, 15.00m, true, true, "Engineering & Consultancy", 30.00m, "153(1)(b)", null },
                    { 25, 30000m, "surveying_soil_testing", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 250, 15.00m, true, true, "Surveying & Soil Testing", 30.00m, "153(1)(b)", null },
                    { 26, 30000m, "machinery_rent", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 260, 15.00m, true, true, "Machinery & Equipment Rent", 30.00m, "153(1)(b)", null },
                    { 27, 30000m, "transport_freight", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 270, 6.00m, true, true, "Transport & Freight", 12.00m, "153(1)(b)", null },
                    { 28, 30000m, "security_services", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 280, 6.00m, true, true, "Security Services", 12.00m, "153(1)(b)", null },
                    { 40, 0m, "office_rent", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 400, 5.00m, true, true, "Office Rent", 10.00m, "155", null },
                    { 41, 0m, "salaries", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 410, 0m, true, false, "Salaries", 0m, "149", null },
                    { 42, 0m, "utilities", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 420, 0m, true, false, "Utilities", 0m, "235", null },
                    { 43, 75000m, "printing_stationery", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 430, 5.00m, true, true, "Printing & Stationery", 10.00m, "153(1)(a)", null },
                    { 44, 75000m, "office_supplies", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 440, 5.00m, true, true, "Office Supplies", 10.00m, "153(1)(a)", null },
                    { 45, 75000m, "office_equipment", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 450, 5.00m, true, true, "Office Equipment", 10.00m, "153(1)(a)", null },
                    { 46, 30000m, "office_renovation", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 460, 7.50m, true, true, "Office Renovation", 15.00m, "153(1)(c)", null },
                    { 47, 30000m, "software_web", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 470, 4.00m, true, true, "Software & Web", 8.00m, "153(1)(b)", null },
                    { 48, 30000m, "marketing_advertisement", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 480, 15.00m, true, true, "Marketing & Advertisement", 30.00m, "153(1)(b)", null },
                    { 49, 30000m, "business_promotion", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 490, 15.00m, true, true, "Business Promotion", 30.00m, "153(1)(b)", null },
                    { 50, 30000m, "legal_professional", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 500, 15.00m, true, true, "Legal & Professional", 30.00m, "153(1)(b)", null },
                    { 51, 30000m, "camera_rent", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 510, 15.00m, true, true, "Camera Equipment Rent", 30.00m, "153(1)(b)", null },
                    { 52, 30000m, "entertainment_food", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 520, 6.00m, true, true, "Entertainment & Food", 12.00m, "153(1)(b)", null },
                    { 53, 30000m, "repairs_maintenance", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 530, 15.00m, true, true, "Repairs & Maintenance", 30.00m, "153(1)(b)", null },
                    { 54, 0m, "rebate_commission", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 540, 12.00m, true, true, "Rebate / Commission", 24.00m, "233", null },
                    { 55, 0m, "site_expenses", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 550, 0m, true, false, "Site Expenses", 0m, null, null },
                    { 56, 0m, "preliminary", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 560, 0m, true, false, "Preliminary", 0m, null, null },
                    { 57, 0m, "donation_charity", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 570, 0m, true, false, "Donation / Charity", 0m, null, null },
                    { 58, 0m, "miscellaneous", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 580, 0m, true, false, "Miscellaneous", 0m, null, null },
                    { 60, 0m, "land_acquisition", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, "Purchase of land or immovable property. Advance tax under s.236K is collected by the registering authority at transfer — the buyer withholds nothing from the seller here.", 5, 0m, true, false, "Land Acquisition", 0m, null, null },
                    { 61, 0m, "permits_approvals", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, "Fees paid to government and regulatory bodies. Payments to government are outside the s.153 withholding regime.", 590, 0m, true, false, "Permits & Approvals", 0m, null, null },
                    { 62, 0m, "taxes_fees", new DateTime(2026, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), null, "Statutory levies and government charges. Not withheld at the point of payment.", 595, 0m, true, false, "Taxes & Fees", 0m, null, null }
                });

            migrationBuilder.InsertData(
                table: "FinanceSettings",
                columns: new[] { "Id", "FinancialYearStartMonth", "UpdatedAt", "WhtRatesConfirmedAt", "WhtRatesConfirmedByName" },
                values: new object[] { 1, 7, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_VendorId_Date",
                table: "Expenses",
                columns: new[] { "VendorId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_WhtApplied_Date",
                table: "Expenses",
                columns: new[] { "WhtApplied", "Date" },
                filter: "[WhtApplied] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Code",
                table: "ExpenseCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_IsActive_DisplayOrder",
                table: "ExpenseCategories",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_TaxSection",
                table: "ExpenseCategories",
                column: "TaxSection");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_IsActive_Name",
                table: "Vendors",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_Name",
                table: "Vendors",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_Ntn",
                table: "Vendors",
                column: "Ntn",
                unique: true,
                filter: "[Ntn] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhtDeposits_ChallanNumber",
                table: "WhtDeposits",
                column: "ChallanNumber",
                unique: true,
                filter: "[ChallanNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhtDeposits_DepositDate",
                table: "WhtDeposits",
                column: "DepositDate");

            migrationBuilder.CreateIndex(
                name: "IX_WhtDeposits_FinanceAccountId",
                table: "WhtDeposits",
                column: "FinanceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Vendors_VendorId",
                table: "Expenses",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Backfill ────────────────────────────────────────────────────────────
            //
            // Existing expenses keep everything they already said. The Category and Vendor text
            // columns were always the record of what a payment was for, and they stay exactly as
            // typed — the new CategoryId/VendorId are additions beside them, not replacements.
            //
            // Historic rows are deliberately NOT assessed for withholding: WhtAmount stays 0 and
            // net paid stays equal to gross. Computing tax on a past payment would restate periods
            // that have already been filed with FBR, and the money was in fact paid in full.
            // The column defaults above already give every existing row exactly that, so there is
            // nothing to write for it here.
            //
            // What this does do is link historic rows to a seeded head wherever the typed text
            // matches one by name. "Land Acquisition", "Permits & Approvals" and "Taxes & Fees"
            // are seeded under their exact former names for this reason — they were heads the
            // expense form already offered, and none of them has a close equivalent among the
            // construction categories. Anything that does not match stays free text and keeps
            // working untouched.
            //
            // WhtTaxSection is stamped at the same time, and that matters more than it looks.
            // Annual thresholds aggregate a vendor's spend BY SECTION, so a row linked to a
            // category but left with a null section is structurally migrated yet invisible to the
            // threshold rule — it would silently fail to count towards the allowance it belongs
            // to. Recording the section is a statement about what the payment was for, not a
            // claim that tax was withheld: WhtApplied and WhtAmount stay false and 0, so these
            // rows still never appear in the withholding reports.
            migrationBuilder.Sql(@"
                UPDATE e
                SET e.CategoryId = c.Id,
                    e.WhtTaxSection = CASE WHEN c.IsWhtApplicable = 1 THEN c.TaxSection ELSE NULL END
                FROM Expenses e
                INNER JOIN ExpenseCategories c ON c.Name = e.Category
                WHERE e.CategoryId IS NULL;");

            // Vendors are deliberately NOT created from the distinct free-text names. Those values
            // were typed by hand over time ('ABC Traders', 'ABC Trader', 'abc traders'), so
            // importing them would manufacture duplicate suppliers that then need a manual merge —
            // and would attach a filer status nobody has verified to every one of them. Vendor
            // records are created deliberately from Finance ▸ Settings instead, and the expense
            // form keeps accepting free text for one-off payees.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Vendors_VendorId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "FinanceSettings");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "WhtDeposits");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_VendorId_Date",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_WhtApplied_Date",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VendorFilerStatusAtEntry",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtAmount",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtApplied",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtOverrideReason",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtRate",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtRateOverridden",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "WhtTaxSection",
                table: "Expenses");
        }
    }
}
