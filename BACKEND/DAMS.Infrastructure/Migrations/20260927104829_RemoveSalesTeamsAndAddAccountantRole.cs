using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSalesTeamsAndAddAccountantRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AssignedEmployeeId is left as it is, so a lead keeps its owner.
            // Before this runs on a production copy, list leads that had a team and no
            // employee so they can be assigned by hand:
            //   SELECT Id, LeadReference, AssignedTeamId
            //   FROM Leads
            //   WHERE AssignedTeamId IS NOT NULL AND AssignedEmployeeId IS NULL;
            // A send addressed to one team has no one left to go to. Any that has not finished
            // is cancelled rather than widened to a bigger group; sent ones stay as history.
            // AudienceType 5 is Team; Status 0/1/5 are Scheduled/Processing/Cancelled.
            migrationBuilder.Sql("""
                UPDATE [dbo].[NotificationJobs]
                SET [Status] = 5,
                    [CancelledAt] = SYSUTCDATETIME(),
                    [FailureReason] = N'Cancelled because sales teams were removed.',
                    [LockedUntil] = NULL,
                    [LockedBy] = NULL
                WHERE [AudienceType] = 5 AND [Status] IN (0, 1);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Teams_TeamId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Teams_AssignedTeamId",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Leads_AssignedTeamId_Stage",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TeamId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "AssignedTeamId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "AssignedTeamId",
                table: "LeadAssignmentHistories");

            migrationBuilder.DropColumn(
                name: "PreviousTeamId",
                table: "LeadAssignmentHistories");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Employees");

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "RoleId", "Role_name" },
                values: new object[] { 5, "Accountant" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "RoleId",
                keyValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "AssignedTeamId",
                table: "Leads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedTeamId",
                table: "LeadAssignmentHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousTeamId",
                table: "LeadAssignmentHistories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ManagerEmployeeId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Employees_ManagerEmployeeId",
                        column: x => x.ManagerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedTeamId_Stage",
                table: "Leads",
                columns: new[] { "AssignedTeamId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TeamId",
                table: "Employees",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ManagerEmployeeId",
                table: "Teams",
                column: "ManagerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Teams_TeamId",
                table: "Employees",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Teams_AssignedTeamId",
                table: "Leads",
                column: "AssignedTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
