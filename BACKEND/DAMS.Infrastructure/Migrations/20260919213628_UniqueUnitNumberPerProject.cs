using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// One unit number, one apartment, per project.
    /// <para>
    /// A unit number is how an apartment is identified everywhere it matters — the booking, the
    /// payment schedule, the customer's statement, the project's availability list. Nothing stopped
    /// a second <c>Units</c> row carrying a number a project already used, and because every
    /// availability guard is written per <c>UnitId</c> (<c>UnitStatus</c> on the row, and the
    /// "no active booking" check keyed on <c>Bookings.UnitId</c>), the duplicate row is invisible
    /// to all of them. Two customers can then each hold a confirmed booking on the same apartment,
    /// each one perfectly valid on its own, with no overlap reported anywhere.
    /// </para>
    /// <para>
    /// The unique index is the guarantee; the check in <c>UnitService</c> is only there to explain
    /// the refusal. Existing duplicates are NOT merged here: which of the rows is the real apartment
    /// depends on the bookings, payments and media hanging off each one, and a migration guessing
    /// wrong would strand money against a unit nobody sold. The guard below names them instead and
    /// stops, so the duplicates are resolved deliberately before the constraint goes on.
    /// </para>
    /// </summary>
    public partial class UniqueUnitNumberPerProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Units] GROUP BY [ProjectId], [UnitNumber] HAVING COUNT(*) > 1)
                BEGIN
                    DECLARE @duplicates nvarchar(2000) = STUFF((
                        SELECT TOP (20) N', project ' + CAST([ProjectId] AS nvarchar(12)) + N' unit ' + [UnitNumber]
                        FROM [Units]
                        GROUP BY [ProjectId], [UnitNumber]
                        HAVING COUNT(*) > 1
                        ORDER BY [ProjectId], [UnitNumber]
                        FOR XML PATH(''), TYPE).value('.', 'nvarchar(2000)'), 1, 2, N'');
                    DECLARE @message nvarchar(4000) = N'Some projects already hold more than one unit with the same unit number, so a unit number cannot be made unique yet: '
                        + @duplicates
                        + N'. Each group is one apartment recorded twice. Move any bookings, payments and media onto the row you are keeping, delete the other, then re-run. Run: SELECT ProjectId, UnitNumber, COUNT(*) FROM Units GROUP BY ProjectId, UnitNumber HAVING COUNT(*) > 1;';
                    THROW 51020, @message, 1;
                END
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Units_ProjectId_UnitNumber",
                table: "Units",
                columns: new[] { "ProjectId", "UnitNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Units_ProjectId_UnitNumber",
                table: "Units");
        }
    }
}
