using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameCancellationForfeitureRevenueHead : Migration
    {
        /// <summary>
        /// Renames the seeded "Cancellation / Forfeiture" manual-revenue head so it can no longer be
        /// mistaken for the ordinary way to record a cancellation. Cancelling a DAMS booking already
        /// recognises the retained amount as income by itself; typing it in again here would count
        /// the same forfeiture twice, and nothing would detect it.
        /// <para>
        /// Guarded rather than a plain UpdateData, on both sides, because revenue heads are editable
        /// by the client from Finance ▸ Settings. It renames the row ONLY while it still carries the
        /// name DAMS gave it, so a client who has already renamed it keeps their wording; and only
        /// while nothing else holds the new name, because Name is uniquely indexed. Code is
        /// untouched, so any row already filed under this head keeps its category.
        /// </para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM RevenueCategories WHERE Id = 10 AND Name = N'Cancellation / Forfeiture')
               AND NOT EXISTS (SELECT 1 FROM RevenueCategories WHERE Name = N'External / Legacy Cancellation Income')
                UPDATE RevenueCategories SET Name = N'External / Legacy Cancellation Income' WHERE Id = 10;
            """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM RevenueCategories WHERE Id = 10 AND Name = N'External / Legacy Cancellation Income')
               AND NOT EXISTS (SELECT 1 FROM RevenueCategories WHERE Name = N'Cancellation / Forfeiture')
                UPDATE RevenueCategories SET Name = N'Cancellation / Forfeiture' WHERE Id = 10;
            """);
    }
}
