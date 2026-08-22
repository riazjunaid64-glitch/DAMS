using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260726003846_SeedLeadStaffRolesBeforeLeadManagement")]
    public partial class SeedLeadStaffRolesBeforeLeadManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This runs immediately before AddLeadManagement on fresh databases. Existing
            // databases that already passed AddLeadManagement still pick it up as a pending,
            // idempotent correction if roles 3 or 4 are missing.
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[Roles]', N'U') IS NOT NULL
                BEGIN
                    BEGIN TRY
                        SET IDENTITY_INSERT [dbo].[Roles] ON;

                        IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [RoleId] = 3)
                        BEGIN
                            INSERT INTO [dbo].[Roles] ([RoleId], [Role_name]) VALUES (3, N'Manager');
                        END

                        IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [RoleId] = 4)
                        BEGIN
                            INSERT INTO [dbo].[Roles] ([RoleId], [Role_name]) VALUES (4, N'Employee');
                        END

                        SET IDENTITY_INSERT [dbo].[Roles] OFF;
                    END TRY
                    BEGIN CATCH
                        SET IDENTITY_INSERT [dbo].[Roles] OFF;
                        THROW;
                    END CATCH
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no-op. These roles may already be referenced by users, and the
            // original lead-management migration is idempotent when they exist.
        }
    }
}
