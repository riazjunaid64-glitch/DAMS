using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerNormalizedPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhone",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Same national number as LeadContactNormalizer.NormalizePhone: digits only, drop a
            // leading 00, drop a leading 92, then drop leading zeros. Existing rows have to be
            // filled here — lookups match this column and nothing else.
            migrationBuilder.Sql("""
                UPDATE c
                SET c.[NormalizedPhone] = n.[NationalNumber]
                FROM [Customers] AS c
                CROSS APPLY (
                    SELECT (
                        SELECT SUBSTRING(c.[Phone], nums.n, 1)
                        FROM (
                            SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                            FROM (VALUES (0),(0),(0),(0),(0),(0),(0),(0),(0),(0)) AS a(x)
                            CROSS JOIN (VALUES (0),(0),(0),(0),(0)) AS b(x)
                        ) AS nums
                        WHERE nums.n <= LEN(c.[Phone])
                          AND SUBSTRING(c.[Phone], nums.n, 1) LIKE '[0-9]'
                        ORDER BY nums.n
                        FOR XML PATH(''), TYPE
                    ) AS DigitsXml
                ) AS extracted
                CROSS APPLY (
                    SELECT CASE
                        WHEN extracted.DigitsXml IS NULL THEN NULL
                        ELSE extracted.DigitsXml.value('.', 'nvarchar(50)')
                    END AS Digits
                ) AS digits
                CROSS APPLY (
                    SELECT CASE
                        WHEN digits.Digits IS NULL OR digits.Digits = N'' THEN NULL
                        WHEN digits.Digits LIKE N'00%' THEN SUBSTRING(digits.Digits, 3, 50)
                        ELSE digits.Digits
                    END AS AfterTrunk
                ) AS trunk
                CROSS APPLY (
                    SELECT CASE
                        WHEN trunk.AfterTrunk IS NULL OR trunk.AfterTrunk = N'' THEN NULL
                        WHEN trunk.AfterTrunk LIKE N'92%' AND LEN(trunk.AfterTrunk) > 2 THEN SUBSTRING(trunk.AfterTrunk, 3, 50)
                        ELSE trunk.AfterTrunk
                    END AS AfterCountry
                ) AS country
                CROSS APPLY (
                    SELECT CASE
                        WHEN country.AfterCountry IS NULL OR country.AfterCountry = N'' THEN NULL
                        WHEN PATINDEX(N'%[^0]%', country.AfterCountry) = 0 THEN NULL
                        ELSE SUBSTRING(country.AfterCountry, PATINDEX(N'%[^0]%', country.AfterCountry), 50)
                    END AS [NationalNumber]
                ) AS n;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedPhone",
                table: "Customers",
                column: "NormalizedPhone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedPhone",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "NormalizedPhone",
                table: "Customers");
        }
    }
}
