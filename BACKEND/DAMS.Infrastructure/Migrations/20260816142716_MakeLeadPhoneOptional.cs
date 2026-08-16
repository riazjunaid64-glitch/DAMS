using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Makes a lead's phone number optional, because leads arriving from ad platforms
    /// frequently have none and inventing one would corrupt duplicate detection.
    ///
    /// Up is metadata-only on SQL Server: existing values are untouched and the
    /// IX_Leads_NormalizedPhone index is unaffected.
    ///
    /// TREAT AS FORWARD-ONLY IN PRODUCTION. Down restores NOT NULL with a default of "",
    /// which would silently stamp an empty string onto every lead that legitimately has no
    /// phone number. Reverting therefore needs those rows dealt with deliberately first.
    /// </summary>
    public partial class MakeLeadPhoneOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Leads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedPhone",
                table: "Leads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The AlterColumn calls below would otherwise silently stamp "" onto every lead that
            // legitimately has no phone number the moment NOT NULL is restored. Refusing first
            // means a revert surfaces the decision to a human instead of quietly rewriting data.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Leads] WHERE [Phone] IS NULL OR [NormalizedPhone] IS NULL)
                BEGIN
                    RAISERROR (
                        'Cannot revert MakeLeadPhoneOptional: one or more Leads have a NULL Phone or NormalizedPhone. Assign or clear these rows deliberately before restoring NOT NULL.',
                        16, 1);
                END
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Leads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedPhone",
                table: "Leads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
