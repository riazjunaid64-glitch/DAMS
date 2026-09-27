using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalizeLeadApartmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadApartmentTypeMigrationNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    PropertyType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NotedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadApartmentTypeMigrationNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadApartmentTypeMigrationNotes_LeadId",
                table: "LeadApartmentTypeMigrationNotes",
                column: "LeadId",
                unique: true);

            // Same compact keys as UnitTypes.Canonical. Created only for this script, then dropped.
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION dbo.Dams_CanonicalLeadApartmentType(@value nvarchar(max))
                RETURNS nvarchar(100)
                AS
                BEGIN
                    IF @value IS NULL OR LTRIM(RTRIM(@value)) = N''
                        RETURN NULL;

                    DECLARE @trimmed nvarchar(max) = LTRIM(RTRIM(@value));
                    DECLARE @len int = DATALENGTH(@trimmed) / 2;
                    DECLARE @i int = 1;
                    DECLARE @compact nvarchar(max) = N'';
                    DECLARE @c nchar(1);

                    WHILE @i <= @len
                    BEGIN
                        SET @c = SUBSTRING(@trimmed, @i, 1);
                        IF @c LIKE N'[0-9A-Za-z]'
                            SET @compact = @compact + @c;
                        SET @i = @i + 1;
                    END

                    SET @compact = LOWER(@compact);

                    RETURN CASE @compact
                        WHEN N'studio' THEN N'Studio'
                        WHEN N'studioapartment' THEN N'Studio'
                        WHEN N'1bed' THEN N'1 Bed'
                        WHEN N'1bedroom' THEN N'1 Bed'
                        WHEN N'1bedroomapartment' THEN N'1 Bed'
                        WHEN N'onebedroom' THEN N'1 Bed'
                        WHEN N'onebedroomapartment' THEN N'1 Bed'
                        WHEN N'1bhk' THEN N'1 Bed'
                        WHEN N'2bed' THEN N'2 Bed'
                        WHEN N'2bedroom' THEN N'2 Bed'
                        WHEN N'2bedroomapartment' THEN N'2 Bed'
                        WHEN N'twobedroom' THEN N'2 Bed'
                        WHEN N'twobedroomapartment' THEN N'2 Bed'
                        WHEN N'2bhk' THEN N'2 Bed'
                        WHEN N'3bed' THEN N'3 Bed'
                        WHEN N'3bedroom' THEN N'3 Bed'
                        WHEN N'3bedroomapartment' THEN N'3 Bed'
                        WHEN N'threebedroom' THEN N'3 Bed'
                        WHEN N'threebedroomapartment' THEN N'3 Bed'
                        WHEN N'3bhk' THEN N'3 Bed'
                        WHEN N'parkingspace' THEN N'Parking space'
                        WHEN N'parking' THEN N'Parking space'
                        ELSE NULL
                    END
                END
                """);

            // Recognised values are rewritten in place. Anything else is left on the lead and
            // recorded here, so a person can fix it. A second run changes nothing: already
            // canonical rows compare equal, and a lead already noted is not noted again.
            migrationBuilder.Sql("""
                UPDATE Leads
                SET PropertyType = dbo.Dams_CanonicalLeadApartmentType(PropertyType)
                WHERE PropertyType IS NOT NULL
                  AND dbo.Dams_CanonicalLeadApartmentType(PropertyType) IS NOT NULL
                  AND PropertyType COLLATE Latin1_General_CS_AS
                      <> dbo.Dams_CanonicalLeadApartmentType(PropertyType) COLLATE Latin1_General_CS_AS;

                INSERT INTO LeadApartmentTypeMigrationNotes (LeadId, PropertyType, NotedAt)
                SELECT l.Id, l.PropertyType, SYSUTCDATETIME()
                FROM Leads l
                WHERE l.PropertyType IS NOT NULL
                  AND dbo.Dams_CanonicalLeadApartmentType(l.PropertyType) IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM LeadApartmentTypeMigrationNotes n WHERE n.LeadId = l.Id);

                DECLARE @forms CURSOR;
                DECLARE @changes TABLE (Path nvarchar(200) NOT NULL PRIMARY KEY, Canonical nvarchar(100) NOT NULL);
                DECLARE @id int, @json nvarchar(max), @original nvarchar(max);
                DECLARE @path nvarchar(200), @canonical nvarchar(100);

                SET @forms = CURSOR LOCAL FAST_FORWARD FOR
                    SELECT Id, AnswerMappingsJson
                    FROM ExternalLeadFormMappings
                    WHERE AnswerMappingsJson IS NOT NULL AND ISJSON(AnswerMappingsJson) = 1;

                OPEN @forms;
                FETCH NEXT FROM @forms INTO @id, @json;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @original = @json;
                    DELETE FROM @changes;

                    INSERT INTO @changes (Path, Canonical)
                    SELECT CONCAT(N'$[', a.[key], N'].Options[', o.[key], N'].Value'),
                           dbo.Dams_CanonicalLeadApartmentType(JSON_VALUE(o.[value], N'$.Value'))
                    FROM OPENJSON(@json) a
                    CROSS APPLY OPENJSON(JSON_QUERY(a.[value], N'$.Options')) o
                    WHERE JSON_VALUE(a.[value], N'$.Target') IN (N'PropertyType', N'1')
                      AND dbo.Dams_CanonicalLeadApartmentType(JSON_VALUE(o.[value], N'$.Value')) IS NOT NULL
                      AND (
                          JSON_VALUE(o.[value], N'$.Value') IS NULL
                          OR dbo.Dams_CanonicalLeadApartmentType(JSON_VALUE(o.[value], N'$.Value'))
                             COLLATE Latin1_General_CS_AS
                             <> JSON_VALUE(o.[value], N'$.Value') COLLATE Latin1_General_CS_AS);

                    WHILE EXISTS (SELECT 1 FROM @changes)
                    BEGIN
                        SELECT TOP (1) @path = Path, @canonical = Canonical FROM @changes;
                        SET @json = JSON_MODIFY(@json, @path, @canonical);
                        DELETE FROM @changes WHERE Path = @path;
                    END

                    IF @original COLLATE Latin1_General_CS_AS <> @json COLLATE Latin1_General_CS_AS
                        UPDATE ExternalLeadFormMappings SET AnswerMappingsJson = @json WHERE Id = @id;

                    FETCH NEXT FROM @forms INTO @id, @json;
                END

                CLOSE @forms;
                DEALLOCATE @forms;
                """);

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.Dams_CanonicalLeadApartmentType;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recognised values are not restored: the previous wording is not kept.
            // Same approach as the unit-type migration.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.Dams_CanonicalLeadApartmentType;");

            migrationBuilder.DropTable(
                name: "LeadApartmentTypeMigrationNotes");
        }
    }
}
