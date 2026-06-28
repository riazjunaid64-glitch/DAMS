using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCustomerSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAt",
                table: "Customers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingDate",
                table: "Bookings",
                column: "BookingDate");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_BookingDate",
                table: "Bookings",
                columns: new[] { "Status", "BookingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CreatedAt",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_BookingDate",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Status_BookingDate",
                table: "Bookings");
        }
    }
}
