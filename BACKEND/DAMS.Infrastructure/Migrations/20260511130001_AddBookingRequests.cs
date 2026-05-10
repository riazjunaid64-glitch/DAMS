using System;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260511130001_AddBookingRequests")]
public partial class AddBookingRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BookingRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UnitId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<int>(type: "int", nullable: true),
                FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CNIC = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookingRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookingRequests_Units_UnitId",
                    column: x => x.UnitId,
                    principalTable: "Units",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BookingRequests_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.NoAction);
                table.ForeignKey(
                    name: "FK_BookingRequests_Users_ReviewedByUserId",
                    column: x => x.ReviewedByUserId,
                    principalTable: "Users",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BookingRequests_UnitId",
            table: "BookingRequests",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_BookingRequests_UserId",
            table: "BookingRequests",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_BookingRequests_Status",
            table: "BookingRequests",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BookingRequests_RequestedAt",
            table: "BookingRequests",
            column: "RequestedAt");

        migrationBuilder.CreateIndex(
            name: "IX_BookingRequests_ReviewedByUserId",
            table: "BookingRequests",
            column: "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BookingRequests");
    }
}
