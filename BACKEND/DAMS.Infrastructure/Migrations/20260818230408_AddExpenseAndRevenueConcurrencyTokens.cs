using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseAndRevenueConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Brings expenses and manual revenue up to the concurrency protection every other mutable
        /// financial record already had. Editing either moves a P&amp;L figure and a bank balance, so
        /// two admins with the same row open could previously overwrite one another with no error and
        /// no trace — the surviving figure being whichever save happened to land second.
        /// <para>
        /// SQL Server populates a new <c>rowversion</c> column on every existing row as part of the
        /// ALTER, so history needs no backfill and gets a valid token immediately. The declared
        /// default is inert (the type generates its own values) and exists only because EF requires
        /// one for a non-nullable added column.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ManualRevenues",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Expenses",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ManualRevenues");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Expenses");
        }
    }
}
