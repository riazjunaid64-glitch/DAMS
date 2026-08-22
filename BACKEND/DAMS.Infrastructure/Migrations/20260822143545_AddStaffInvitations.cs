using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "AccountStatus",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Every login that existed before staff invitations keeps signing in. The column
            // default already writes 0, but stating it here means the guarantee survives a
            // later renumbering of UserAccountStatus rather than silently turning every
            // Admin, Client, Manager and Employee into an un-activated account.
            // 0 = UserAccountStatus.Active.
            migrationBuilder.Sql("UPDATE [Users] SET [AccountStatus] = 0;");

            migrationBuilder.CreateTable(
                name: "StaffInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    InvitedByUserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffInvitations_Users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffInvitations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_ExpiresAt",
                table: "StaffInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_InvitedByUserId",
                table: "StaffInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_TokenHash",
                table: "StaffInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffInvitations_UserId_CreatedAt",
                table: "StaffInvitations",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring NOT NULL below makes EF emit "UPDATE [Users] SET [Password] = N''
            // WHERE [Password] IS NULL" first. That does not fail — which is the problem. It
            // silently stamps an empty string into the password column of every login that was
            // granted access but has not activated yet, and the very next statement drops the
            // AccountStatus column that was the only record of them being un-activated. Nothing
            // verifies against N'', so it is not a usable password; it is worse than that, an
            // account left in a state the old code cannot even evaluate.
            //
            // So the rollback refuses instead, and says what has to be decided first. With no
            // outstanding invitations — the ordinary case, rolling back soon after deploying —
            // nothing here triggers and the rollback proceeds exactly as before.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Users] WHERE [Password] IS NULL)
    THROW 51000, N'Cannot roll back AddStaffInvitations: some logins were invited but never activated, so their Users.Password is still NULL. Resolve those accounts first (let them activate, remove them, or give them a password through the application), then re-run this rollback.', 1;
");

            migrationBuilder.DropTable(
                name: "StaffInvitations");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
