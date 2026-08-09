using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDocumentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerDocumentCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsRequiredByDefault = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    AllowedFileTypes = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AssignToNewCustomers = table.Column<bool>(type: "bit", nullable: false),
                    DefaultDueDays = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDocumentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDocumentRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    AllowedFileTypes = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostponedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActionByUserId = table.Column<int>(type: "int", nullable: true),
                    LastActionByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDocumentRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentRequirements_CustomerDocumentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CustomerDocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentRequirements_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDocumentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequirementId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentVersions_CustomerDocumentRequirements_RequirementId",
                        column: x => x.RequirementId,
                        principalTable: "CustomerDocumentRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDocumentAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    RequirementId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: true),
                    PerformedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDocumentAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentAuditEntries_CustomerDocumentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CustomerDocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentAuditEntries_CustomerDocumentRequirements_RequirementId",
                        column: x => x.RequirementId,
                        principalTable: "CustomerDocumentRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentAuditEntries_CustomerDocumentVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "CustomerDocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomerDocumentAuditEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CustomerDocumentCategories",
                columns: new[] { "Id", "AllowedFileTypes", "AssignToNewCustomers", "Code", "CreatedAt", "CreatedByName", "CreatedByUserId", "DefaultDueDays", "Description", "DisplayOrder", "IsActive", "IsRequiredByDefault", "MaxFileSizeBytes", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, ".pdf,.jpg,.jpeg,.png", true, "cnic_front", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 10, true, true, 10485760L, "CNIC Front", null },
                    { 2, ".pdf,.jpg,.jpeg,.png", true, "cnic_back", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 20, true, true, 10485760L, "CNIC Back", null },
                    { 3, ".pdf,.jpg,.jpeg,.png", true, "customer_photo", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 30, true, true, 10485760L, "Customer Photograph", null },
                    { 4, ".pdf,.jpg,.jpeg,.png", true, "proof_of_address", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 40, true, false, 10485760L, "Proof of Address", null },
                    { 5, ".pdf,.jpg,.jpeg,.png", true, "passport", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 50, true, false, 10485760L, "Passport", null },
                    { 6, ".pdf,.jpg,.jpeg,.png", true, "next_of_kin_cnic", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 60, true, false, 10485760L, "Next-of-Kin CNIC", null },
                    { 7, ".pdf,.jpg,.jpeg,.png", true, "signature_specimen", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 70, true, false, 10485760L, "Signature Specimen", null },
                    { 8, ".pdf,.jpg,.jpeg,.png", true, "tax_document", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 80, true, false, 10485760L, "Tax Document", null },
                    { 9, ".pdf,.jpg,.jpeg,.png", true, "other", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, 90, true, false, 10485760L, "Other", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAuditEntries_CategoryId",
                table: "CustomerDocumentAuditEntries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAuditEntries_CustomerId_OccurredAt",
                table: "CustomerDocumentAuditEntries",
                columns: new[] { "CustomerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAuditEntries_RequirementId_OccurredAt",
                table: "CustomerDocumentAuditEntries",
                columns: new[] { "RequirementId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAuditEntries_VersionId",
                table: "CustomerDocumentAuditEntries",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentCategories_AssignToNewCustomers",
                table: "CustomerDocumentCategories",
                column: "AssignToNewCustomers");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentCategories_Code",
                table: "CustomerDocumentCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentCategories_IsActive_DisplayOrder",
                table: "CustomerDocumentCategories",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentRequirements_CategoryId",
                table: "CustomerDocumentRequirements",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentRequirements_CustomerId_CategoryId",
                table: "CustomerDocumentRequirements",
                columns: new[] { "CustomerId", "CategoryId" },
                unique: true,
                filter: "[CategoryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentRequirements_CustomerId_CustomName",
                table: "CustomerDocumentRequirements",
                columns: new[] { "CustomerId", "Name" },
                unique: true,
                filter: "[CategoryId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentRequirements_CustomerId_DisplayOrder",
                table: "CustomerDocumentRequirements",
                columns: new[] { "CustomerId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentRequirements_CustomerId_Status",
                table: "CustomerDocumentRequirements",
                columns: new[] { "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentVersions_RequirementId_Current",
                table: "CustomerDocumentVersions",
                column: "RequirementId",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentVersions_RequirementId_VersionNumber",
                table: "CustomerDocumentVersions",
                columns: new[] { "RequirementId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentVersions_UploadedAt",
                table: "CustomerDocumentVersions",
                column: "UploadedAt");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerDocumentAuditEntries");

            migrationBuilder.DropTable(
                name: "CustomerDocumentVersions");

            migrationBuilder.DropTable(
                name: "CustomerDocumentRequirements");

            migrationBuilder.DropTable(
                name: "CustomerDocumentCategories");

        }
    }
}
