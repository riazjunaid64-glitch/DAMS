using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeBookingRequestIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_Status_RequestedAt",
                table: "BookingRequests",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_UnitId_Status",
                table: "BookingRequests",
                columns: new[] { "UnitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_UserId_RequestedAt",
                table: "BookingRequests",
                columns: new[] { "UserId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_Status_RequestedAt",
                table: "BookingRequests");

            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_UnitId_Status",
                table: "BookingRequests");

            migrationBuilder.DropIndex(
                name: "IX_BookingRequests_UserId_RequestedAt",
                table: "BookingRequests");
        }
    }
}
