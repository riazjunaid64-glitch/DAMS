using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceMediaWithEnterpriseMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns to ProjectMedias table
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "ProjectMedias",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsCover",
                table: "ProjectMedias",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "ProjectMedias",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "ProjectMedias",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProjectMedias",
                type: "nvarchar(1000)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "ProjectMedias",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "ProjectMedias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "ProjectMedias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "ProjectMedias",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "ProjectMedias",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ProjectMedias",
                type: "datetime2",
                nullable: true);

            // Add MediaType column if it doesn't exist
            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "ProjectMedias",
                type: "nvarchar(50)",
                nullable: false,
                defaultValue: "");

            // Add new columns to UnitMedias table
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "UnitMedias",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsCover",
                table: "UnitMedias",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "UnitMedias",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "UnitMedias",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "UnitMedias",
                type: "nvarchar(1000)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "UnitMedias",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "UnitMedias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "UnitMedias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "UnitMedias",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "UnitMedias",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "UnitMedias",
                type: "datetime2",
                nullable: true);

            // UnitMedias.MediaType already exists from AddProjectModule — do not add again

            // Create indexes for ProjectMedias
            migrationBuilder.CreateIndex(
                name: "IX_ProjectMedias_ProjectId_IsCover",
                table: "ProjectMedias",
                columns: new[] { "ProjectId", "IsCover" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMedias_ProjectId_DisplayOrder",
                table: "ProjectMedias",
                columns: new[] { "ProjectId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMedias_ProjectId_Category",
                table: "ProjectMedias",
                columns: new[] { "ProjectId", "Category" });

            // Create indexes for UnitMedias
            migrationBuilder.CreateIndex(
                name: "IX_UnitMedias_UnitId_IsCover",
                table: "UnitMedias",
                columns: new[] { "UnitId", "IsCover" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitMedias_UnitId_DisplayOrder",
                table: "UnitMedias",
                columns: new[] { "UnitId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitMedias_UnitId_Category",
                table: "UnitMedias",
                columns: new[] { "UnitId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes for ProjectMedias
            migrationBuilder.DropIndex(
                name: "IX_ProjectMedias_ProjectId_Category",
                table: "ProjectMedias");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMedias_ProjectId_DisplayOrder",
                table: "ProjectMedias");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMedias_ProjectId_IsCover",
                table: "ProjectMedias");

            // Drop indexes for UnitMedias
            migrationBuilder.DropIndex(
                name: "IX_UnitMedias_UnitId_Category",
                table: "UnitMedias");

            migrationBuilder.DropIndex(
                name: "IX_UnitMedias_UnitId_DisplayOrder",
                table: "UnitMedias");

            migrationBuilder.DropIndex(
                name: "IX_UnitMedias_UnitId_IsCover",
                table: "UnitMedias");

            // Remove columns from ProjectMedias
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "IsCover",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ProjectMedias");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "ProjectMedias");

            // Remove columns from UnitMedias
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "IsCover",
                table: "UnitMedias");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "UnitMedias");
        }
    }
}
