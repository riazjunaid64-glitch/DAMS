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
    /// <para>
    /// Stored unit numbers ARE rewritten, to the trimmed form <c>UnitService</c> now saves. That is
    /// not cosmetic: the constraint is only worth having if the stored key means the same thing the
    /// application means, and a surviving untrimmed value would slip straight back through it.
    /// </para>
    /// </summary>
    public partial class UniqueUnitNumberPerProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The identity a unit number is compared on is the TRIMMED value, because that is what
            // UnitService writes from here on. The stored column is not trimmed: until this branch
            // the service saved dto.UnitNumber verbatim, so ' A-101' is ordinary historical data.
            // Leading whitespace is significant to SQL Server, so an untrimmed key would let
            // ' A-101' and a freshly trimmed 'A-101' sit side by side under a unique index and be
            // two bookable records of one apartment again — precisely what this migration is for.
            // (Trailing spaces would collide on their own: SQL Server ignores them when comparing.
            // Leading ones do not, which is the hole.)
            //
            // So the column is canonicalised to match the service, and both the collision check and
            // the index are taken on that canonical form. TRIM(chars FROM x) needs SQL Server 2017
            // or later.
            //
            // The character set below is not "the usual spaces" — it is EXACTLY the 25 code points
            // char.IsWhiteSpace returns true for, which is what string.Trim() removes. A shorter
            // list would put the two sides back out of step for anything it omitted: a legacy
            // U+3000 or U+2009 would survive the trim here, be stripped by the service on the next
            // save, and the same apartment would hold two rows under a unique index that sees two
            // different keys. Note U+200B ZERO WIDTH SPACE is deliberately absent — .NET does not
            // treat it as whitespace, so neither may this.
            migrationBuilder.Sql("""
                DECLARE @trim nvarchar(50) =
                    CHAR(9) + CHAR(10) + CHAR(11) + CHAR(12) + CHAR(13) + N' '
                    + NCHAR(133) + NCHAR(160) + NCHAR(5760)
                    + NCHAR(8192) + NCHAR(8193) + NCHAR(8194) + NCHAR(8195) + NCHAR(8196)
                    + NCHAR(8197) + NCHAR(8198) + NCHAR(8199) + NCHAR(8200) + NCHAR(8201)
                    + NCHAR(8202) + NCHAR(8232) + NCHAR(8233) + NCHAR(8239) + NCHAR(8287)
                    + NCHAR(12288);

                IF EXISTS (SELECT 1 FROM [Units]
                           GROUP BY [ProjectId], TRIM(@trim FROM [UnitNumber]) HAVING COUNT(*) > 1)
                BEGIN
                    DECLARE @duplicates nvarchar(2000) = STUFF((
                        SELECT TOP (20) N', project ' + CAST([ProjectId] AS nvarchar(12))
                            + N' unit ' + TRIM(@trim FROM [UnitNumber])
                        FROM [Units]
                        GROUP BY [ProjectId], TRIM(@trim FROM [UnitNumber])
                        HAVING COUNT(*) > 1
                        ORDER BY [ProjectId], TRIM(@trim FROM [UnitNumber])
                        FOR XML PATH(''), TYPE).value('.', 'nvarchar(2000)'), 1, 2, N'');
                    DECLARE @message nvarchar(4000) = N'Some projects already hold more than one unit with the same unit number, so a unit number cannot be made unique yet: '
                        + @duplicates
                        + N'. Each group is one apartment recorded twice (surrounding spaces are ignored when comparing). Move any bookings, payments and media onto the row you are keeping, delete the other, then re-run. Run: SELECT ProjectId, TRIM(UnitNumber) AS UnitNumber, COUNT(*) FROM Units GROUP BY ProjectId, TRIM(UnitNumber) HAVING COUNT(*) > 1;';
                    THROW 51020, @message, 1;
                END

                -- No collisions, so this only rewrites a row into the form the service would now
                -- save it in. Done before the index so the index is built on canonical values.
                UPDATE [Units] SET [UnitNumber] = TRIM(@trim FROM [UnitNumber])
                WHERE [UnitNumber] <> TRIM(@trim FROM [UnitNumber]) COLLATE Latin1_General_BIN2;
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
