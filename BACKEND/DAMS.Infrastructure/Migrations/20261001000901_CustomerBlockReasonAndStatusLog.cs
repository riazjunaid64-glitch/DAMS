using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomerBlockReasonAndStatusLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedAt",
                table: "Customers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockedByUserId",
                table: "Customers",
                type: "int",
                nullable: true);

            // "Inactive" meant nothing anywhere in the system; every such customer is Active now.
            migrationBuilder.Sql("UPDATE [Customers] SET [Status] = 0 WHERE [Status] = 1;");

            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "Customers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerStatusLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ByUserId = table.Column<int>(type: "int", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerStatusLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerStatusLogs_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerStatusLogs_CustomerId_At",
                table: "CustomerStatusLogs",
                columns: new[] { "CustomerId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerStatusLogs");

            migrationBuilder.DropColumn(
                name: "BlockedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BlockedByUserId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "Customers");
        }
    }
}
