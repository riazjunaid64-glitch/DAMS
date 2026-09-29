namespace DAMS.Infrastructure.Migrations;

/// <summary>
/// SQL backfill for <see cref="AddCustomerNormalizedPhone"/>. Mirrors
/// <c>DAMS.Domain.Common.ContactNormalization.NormalizePhone</c> for Pakistani numbers.
/// </summary>
internal static class CustomerNormalizedPhoneBackfillSql
{
    public const string Up = """
        CREATE OR ALTER FUNCTION [dbo].[DAMS_NormalizeCustomerPhone](@phone NVARCHAR(50))
        RETURNS NVARCHAR(50)
        AS
        BEGIN
            DECLARE @digits NVARCHAR(50) = N'';
            DECLARE @i INT = 1;
            DECLARE @ch NCHAR(1);
            WHILE @i <= LEN(@phone)
            BEGIN
                SET @ch = SUBSTRING(@phone, @i, 1);
                IF @ch BETWEEN N'0' AND N'9'
                    SET @digits = @digits + @ch;
                SET @i = @i + 1;
            END;

            IF @digits = N'' RETURN NULL;

            IF LEFT(@digits, 2) = N'00'
                SET @digits = SUBSTRING(@digits, 3, LEN(@digits) - 2);

            IF LEFT(@digits, 2) = N'92' AND LEN(@digits) > 2
                SET @digits = SUBSTRING(@digits, 3, LEN(@digits) - 2);

            WHILE LEFT(@digits, 1) = N'0' AND LEN(@digits) > 0
                SET @digits = SUBSTRING(@digits, 2, LEN(@digits) - 1);

            IF @digits = N'' RETURN NULL;
            RETURN @digits;
        END;
        """;

    public const string Backfill = """
        UPDATE [Customers]
        SET [NormalizedPhone] = [dbo].[DAMS_NormalizeCustomerPhone]([Phone])
        WHERE [Phone] IS NOT NULL AND LTRIM(RTRIM([Phone])) <> N'';
        """;

    public const string Down = """
        DROP FUNCTION IF EXISTS [dbo].[DAMS_NormalizeCustomerPhone];
        """;
}
