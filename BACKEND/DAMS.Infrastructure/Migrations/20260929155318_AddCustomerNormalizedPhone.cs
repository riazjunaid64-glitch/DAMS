using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerNormalizedPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhone",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedPhone",
                table: "Customers",
                column: "NormalizedPhone");

            migrationBuilder.Sql(CustomerNormalizedPhoneBackfillSql.Up);
            migrationBuilder.Sql(CustomerNormalizedPhoneBackfillSql.Backfill);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CustomerNormalizedPhoneBackfillSql.Down);

            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedPhone",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "NormalizedPhone",
                table: "Customers");
        }
    }
}
