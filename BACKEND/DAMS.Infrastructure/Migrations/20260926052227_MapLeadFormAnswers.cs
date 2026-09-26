using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MapLeadFormAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentPreference",
                table: "Leads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExternalLeadFormMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FormExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InterestedProjectId = table.Column<int>(type: "int", nullable: true),
                    AnswerMappingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLeadFormMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalLeadFormMappings_Projects_InterestedProjectId",
                        column: x => x.InterestedProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLeadFormMappings_InterestedProjectId",
                table: "ExternalLeadFormMappings",
                column: "InterestedProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLeadFormMappings_Provider_FormExternalId",
                table: "ExternalLeadFormMappings",
                columns: new[] { "Provider", "FormExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalLeadFormMappings");

            migrationBuilder.DropColumn(
                name: "PaymentPreference",
                table: "Leads");
        }
    }
}
