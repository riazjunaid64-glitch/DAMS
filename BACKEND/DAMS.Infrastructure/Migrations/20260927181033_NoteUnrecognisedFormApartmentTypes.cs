using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NoteUnrecognisedFormApartmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadFormApartmentTypeMigrationNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalLeadFormMappingId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FormExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    QuestionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OptionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NotedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadFormApartmentTypeMigrationNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadFormApartmentTypeMigrationNotes_ExternalLeadFormMappingId_QuestionKey_OptionKey",
                table: "LeadFormApartmentTypeMigrationNotes",
                columns: new[] { "ExternalLeadFormMappingId", "QuestionKey", "OptionKey" },
                unique: true);

            // Same compact keys as UnitTypes.Canonical. The previous migration drops this
            // helper when it finishes, so the scan below creates it again and drops it too.
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

            // Unrecognised PropertyType answers stay in the mapping. This lists them.
            // A second run inserts nothing: the unique key is already noted.
            migrationBuilder.Sql("""
                INSERT INTO LeadFormApartmentTypeMigrationNotes
                    (ExternalLeadFormMappingId, Provider, FormExternalId, QuestionKey, OptionKey, Value, NotedAt)
                SELECT src.MappingId, src.Provider, src.FormExternalId, src.QuestionKey, src.OptionKey, src.Value, SYSUTCDATETIME()
                FROM (
                    SELECT
                        m.Id AS MappingId,
                        m.Provider,
                        m.FormExternalId,
                        LEFT(JSON_VALUE(a.[value], N'$.QuestionKey'), 200) AS QuestionKey,
                        LEFT(JSON_VALUE(o.[value], N'$.OptionKey'), 200) AS OptionKey,
                        JSON_VALUE(o.[value], N'$.Value') AS Value,
                        ROW_NUMBER() OVER (
                            PARTITION BY m.Id,
                                LEFT(JSON_VALUE(a.[value], N'$.QuestionKey'), 200),
                                LEFT(JSON_VALUE(o.[value], N'$.OptionKey'), 200)
                            ORDER BY o.[key]) AS n
                    FROM ExternalLeadFormMappings m
                    CROSS APPLY OPENJSON(m.AnswerMappingsJson) a
                    CROSS APPLY OPENJSON(JSON_QUERY(a.[value], N'$.Options')) o
                    WHERE ISJSON(m.AnswerMappingsJson) = 1
                      AND JSON_VALUE(a.[value], N'$.Target') IN (N'PropertyType', N'1')
                      AND JSON_VALUE(a.[value], N'$.QuestionKey') IS NOT NULL
                      AND JSON_VALUE(o.[value], N'$.OptionKey') IS NOT NULL
                      AND JSON_VALUE(o.[value], N'$.Value') IS NOT NULL
                      AND LTRIM(RTRIM(JSON_VALUE(o.[value], N'$.Value'))) <> N''
                      AND dbo.Dams_CanonicalLeadApartmentType(JSON_VALUE(o.[value], N'$.Value')) IS NULL
                ) src
                WHERE src.n = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM LeadFormApartmentTypeMigrationNotes note
                      WHERE note.ExternalLeadFormMappingId = src.MappingId
                        AND note.QuestionKey = src.QuestionKey
                        AND note.OptionKey = src.OptionKey);
                """);

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.Dams_CanonicalLeadApartmentType;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.Dams_CanonicalLeadApartmentType;");

            migrationBuilder.DropTable(
                name: "LeadFormApartmentTypeMigrationNotes");
        }
    }
}
