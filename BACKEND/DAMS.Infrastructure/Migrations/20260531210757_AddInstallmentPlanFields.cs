using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPlanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Installments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Installments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentFrequency",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstallmentPlanGeneratedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentPlanGeneratedByUserId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfInstallments",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PossessionAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PossessionDueDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "InstallmentFrequency",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "InstallmentPlanGeneratedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "InstallmentPlanGeneratedByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NumberOfInstallments",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PossessionAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PossessionDueDate",
                table: "Bookings");
        }
    }
}
