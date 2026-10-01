using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSetupAskEveryCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AsksEveryCustomer",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOther",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuppressed",
                table: "CustomerDocumentRequirements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The switch is on only when the old category is active and required. Hidden starts off
            // for every row; "Other" is the seeded catch-all, not a Document setup row.
            migrationBuilder.Sql("""
                UPDATE [CustomerDocumentCategories]
                SET [AsksEveryCustomer] = CASE WHEN [IsActive] = 1 AND [IsRequiredByDefault] = 1 THEN 1 ELSE 0 END,
                    [IsOther] = CASE WHEN [Code] = N'other' THEN 1 ELSE 0 END,
                    [IsHidden] = 0;

                -- The first row of each name stays. Every other copy takes the next free "name (n)",
                -- checked against the whole table. "Twin", "Twin", "Twin (2)" becomes "Twin", "Twin (3)", "Twin (2)".
                DECLARE [duplicates] CURSOR LOCAL FAST_FORWARD FOR
                    SELECT [c].[Id], [c].[Name]
                    FROM [CustomerDocumentCategories] AS [c]
                    WHERE [c].[Id] NOT IN (
                        SELECT MIN([Id]) FROM [CustomerDocumentCategories] GROUP BY LOWER([Name])
                    )
                    ORDER BY [c].[Id];

                DECLARE @id int, @base nvarchar(150), @candidate nvarchar(150), @n int;
                OPEN [duplicates];
                FETCH NEXT FROM [duplicates] INTO @id, @base;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @n = 2;
                    WHILE 1 = 1
                    BEGIN
                        SET @candidate = LEFT(@base, 141) + N' (' + CONVERT(nvarchar(6), @n) + N')';
                        IF NOT EXISTS (
                            SELECT 1 FROM [CustomerDocumentCategories]
                            WHERE [Id] <> @id AND LOWER([Name]) = LOWER(@candidate)
                        )
                            BREAK;
                        SET @n = @n + 1;
                    END
                    UPDATE [CustomerDocumentCategories] SET [Name] = @candidate WHERE [Id] = @id;
                    FETCH NEXT FROM [duplicates] INTO @id, @base;
                END
                CLOSE [duplicates];
                DEALLOCATE [duplicates];
                """);

            migrationBuilder.DropIndex(
                name: "IX_CustomerDocumentCategories_Code",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDocumentCategories_IsActive_DisplayOrder",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDocumentCategories_AssignToNewCustomers",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "IsRequiredByDefault",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "AssignToNewCustomers",
                table: "CustomerDocumentCategories");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentCategories_AsksEveryCustomer",
                table: "CustomerDocumentCategories",
                column: "AsksEveryCustomer");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentCategories_Name",
                table: "CustomerDocumentCategories",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerDocumentCategories_Name",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDocumentCategories_AsksEveryCustomer",
                table: "CustomerDocumentCategories");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "CustomerDocumentCategories",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CustomerDocumentCategories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "CustomerDocumentCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredByDefault",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AssignToNewCustomers",
                table: "CustomerDocumentCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE [CustomerDocumentCategories]
                SET [Code] = CASE [Id]
                        WHEN 1 THEN N'cnic_front'
                        WHEN 2 THEN N'cnic_back'
                        WHEN 3 THEN N'customer_photo'
                        WHEN 4 THEN N'proof_of_address'
                        WHEN 5 THEN N'passport'
                        WHEN 6 THEN N'next_of_kin_cnic'
                        WHEN 7 THEN N'signature_specimen'
                        WHEN 8 THEN N'tax_document'
                        WHEN 9 THEN N'other'
                        ELSE CONCAT(N'restored_', [Id])
                    END,
                    [DisplayOrder] = CASE [Id]
                        WHEN 1 THEN 10 WHEN 2 THEN 20 WHEN 3 THEN 30 WHEN 4 THEN 40 WHEN 5 THEN 50
                        WHEN 6 THEN 60 WHEN 7 THEN 70 WHEN 8 THEN 80 WHEN 9 THEN 90
                        ELSE [Id] * 10
                    END,
                    [IsActive] = CASE WHEN [IsHidden] = 1 THEN 0 ELSE 1 END,
                    [IsRequiredByDefault] = CASE WHEN [Id] = 9 THEN 0 WHEN [AsksEveryCustomer] = 1 THEN 1 ELSE 0 END,
                    [AssignToNewCustomers] = CASE WHEN [Id] BETWEEN 1 AND 9 THEN 1 ELSE [AsksEveryCustomer] END;
                """);

            migrationBuilder.DropColumn(
                name: "AsksEveryCustomer",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "IsOther",
                table: "CustomerDocumentCategories");

            migrationBuilder.DropColumn(
                name: "IsSuppressed",
                table: "CustomerDocumentRequirements");

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
        }
    }
}
