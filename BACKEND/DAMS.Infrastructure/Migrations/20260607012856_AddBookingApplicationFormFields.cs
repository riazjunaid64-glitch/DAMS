using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingApplicationFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Customers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "Customers",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Whatsapp",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApartmentCategory",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApplicationAmountReceived",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApplicationDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationPaymentType",
                table: "Bookings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "Bookings",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCorner",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NextOfKinAddress",
                table: "Bookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextOfKinCnic",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextOfKinContact",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextOfKinDob",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextOfKinName",
                table: "Bookings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextOfKinRelation",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentThrough",
                table: "Bookings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerSft",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceId",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNo",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tower",
                table: "Bookings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Occupation",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Whatsapp",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ApartmentCategory",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ApplicationAmountReceived",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ApplicationDate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ApplicationPaymentType",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsCorner",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinAddress",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinCnic",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinContact",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinDob",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextOfKinRelation",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentThrough",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PricePerSft",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SerialNo",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Tower",
                table: "Bookings");
        }
    }
}
