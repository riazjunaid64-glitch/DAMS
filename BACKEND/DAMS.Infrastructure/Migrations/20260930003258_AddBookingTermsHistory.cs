using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingTermsHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingTermsHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    OldAgreedSalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewAgreedSalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OldDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    NewDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    OldDiscountReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewDiscountReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OldBookingAmountRequired = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewBookingAmountRequired = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OldBookingAmountDueDate = table.Column<DateTime>(type: "date", nullable: true),
                    NewBookingAmountDueDate = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingTermsHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingTermsHistories_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingTermsHistories_BookingId_ChangedAt",
                table: "BookingTermsHistories",
                columns: new[] { "BookingId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingTermsHistories");
        }
    }
}
