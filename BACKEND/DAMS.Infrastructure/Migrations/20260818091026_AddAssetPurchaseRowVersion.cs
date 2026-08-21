using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetPurchaseRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recovers a change the model has claimed since 20260814093000 but the database never
            // received: that migration shipped without its Designer file, so EF never registered it
            // and 'database update' could not apply it. Every read and write of AssetPurchases has
            // been failing on the missing column since.
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AssetPurchases",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AssetPurchases");
        }
    }
}
