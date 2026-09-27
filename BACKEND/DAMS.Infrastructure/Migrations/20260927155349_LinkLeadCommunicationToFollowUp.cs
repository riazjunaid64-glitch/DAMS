using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkLeadCommunicationToFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FollowUpId",
                table: "LeadCommunications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadCommunications_FollowUpId",
                table: "LeadCommunications",
                column: "FollowUpId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeadCommunications_LeadFollowUps_FollowUpId",
                table: "LeadCommunications",
                column: "FollowUpId",
                principalTable: "LeadFollowUps",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeadCommunications_LeadFollowUps_FollowUpId",
                table: "LeadCommunications");

            migrationBuilder.DropIndex(
                name: "IX_LeadCommunications_FollowUpId",
                table: "LeadCommunications");

            migrationBuilder.DropColumn(
                name: "FollowUpId",
                table: "LeadCommunications");
        }
    }
}
