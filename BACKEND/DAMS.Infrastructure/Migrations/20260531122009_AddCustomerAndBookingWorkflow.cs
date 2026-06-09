using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAndBookingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Drop legacy Booking -> User (Client) relationship ---
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Users_ClientId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ClientId",
                table: "Bookings");

            migrationBuilder.DropColumn(name: "ClientId", table: "Bookings");
            migrationBuilder.DropColumn(name: "DownPaymentAmount", table: "Bookings");
            migrationBuilder.DropColumn(name: "UnitPriceAtBooking", table: "Bookings");

            // --- Customers table ---
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CNIC = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(name: "IX_Customers_CNIC", table: "Customers", column: "CNIC");
            migrationBuilder.CreateIndex(name: "IX_Customers_Email", table: "Customers", column: "Email");
            migrationBuilder.CreateIndex(name: "IX_Customers_Phone", table: "Customers", column: "Phone");
            migrationBuilder.CreateIndex(name: "IX_Customers_Status", table: "Customers", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_Customers_UserId", table: "Customers", column: "UserId");

            // --- New Booking columns ---
            migrationBuilder.AddColumn<string>(
                name: "BookingReference",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BookingRequestId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AssignedSalesUserId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AgreedSalePrice",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BookingAmountRequired",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BookingAmountReceived",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInstallmentAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "BookingAmountDueDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BookingAmountConfirmedDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstallmentPlanStartDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PossessionDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNotes",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNotes",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Bookings",
                type: "int",
                nullable: true);

            // --- BookingRequest -> Customer link ---
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "BookingRequests",
                type: "int",
                nullable: true);

            // --- Indexes for Bookings / BookingRequests ---
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingReference",
                table: "Bookings",
                column: "BookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingRequestId",
                table: "Bookings",
                column: "BookingRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CustomerId",
                table: "Bookings",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status",
                table: "Bookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BookingRequests_CustomerId",
                table: "BookingRequests",
                column: "CustomerId");

            // --- Foreign keys ---
            migrationBuilder.AddForeignKey(
                name: "FK_BookingRequests_Customers_CustomerId",
                table: "BookingRequests",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_BookingRequests_BookingRequestId",
                table: "Bookings",
                column: "BookingRequestId",
                principalTable: "BookingRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Customers_CustomerId",
                table: "Bookings",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingRequests_Customers_CustomerId",
                table: "BookingRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_BookingRequests_BookingRequestId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Customers_CustomerId",
                table: "Bookings");

            migrationBuilder.DropTable(name: "Customers");

            migrationBuilder.DropIndex(name: "IX_Bookings_BookingReference", table: "Bookings");
            migrationBuilder.DropIndex(name: "IX_Bookings_BookingRequestId", table: "Bookings");
            migrationBuilder.DropIndex(name: "IX_Bookings_CustomerId", table: "Bookings");
            migrationBuilder.DropIndex(name: "IX_Bookings_Status", table: "Bookings");
            migrationBuilder.DropIndex(name: "IX_BookingRequests_CustomerId", table: "BookingRequests");

            migrationBuilder.DropColumn(name: "CustomerId", table: "BookingRequests");

            migrationBuilder.DropColumn(name: "AgreedSalePrice", table: "Bookings");
            migrationBuilder.DropColumn(name: "AssignedSalesUserId", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingAmountConfirmedDate", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingAmountDueDate", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingAmountReceived", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingAmountRequired", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingReference", table: "Bookings");
            migrationBuilder.DropColumn(name: "BookingRequestId", table: "Bookings");
            migrationBuilder.DropColumn(name: "CompletionDate", table: "Bookings");
            migrationBuilder.DropColumn(name: "CreatedByUserId", table: "Bookings");
            migrationBuilder.DropColumn(name: "CustomerId", table: "Bookings");
            migrationBuilder.DropColumn(name: "CustomerNotes", table: "Bookings");
            migrationBuilder.DropColumn(name: "DiscountAmount", table: "Bookings");
            migrationBuilder.DropColumn(name: "DiscountReason", table: "Bookings");
            migrationBuilder.DropColumn(name: "InstallmentPlanStartDate", table: "Bookings");
            migrationBuilder.DropColumn(name: "InternalNotes", table: "Bookings");
            migrationBuilder.DropColumn(name: "ListPrice", table: "Bookings");
            migrationBuilder.DropColumn(name: "PossessionDate", table: "Bookings");
            migrationBuilder.DropColumn(name: "Source", table: "Bookings");
            migrationBuilder.DropColumn(name: "TotalInstallmentAmount", table: "Bookings");

            // Restore legacy Booking -> User (Client) columns/relationship.
            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DownPaymentAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceAtBooking",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ClientId",
                table: "Bookings",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Users_ClientId",
                table: "Bookings",
                column: "ClientId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
