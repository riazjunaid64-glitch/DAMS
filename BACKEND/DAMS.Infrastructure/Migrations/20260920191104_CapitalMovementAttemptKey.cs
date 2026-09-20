using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Names the one attempt that recorded a capital movement, so a replay recognises its own
    /// committed work.
    /// <para>
    /// The movement is written inside a retrying execution strategy. A commit that reached SQL
    /// Server but whose acknowledgement was lost looks exactly like one that never happened, so the
    /// strategy replays the delegate — and the row it would insert again carries a store-generated
    /// Id that remembers nothing about the attempt that asked for it. The contribution is banked
    /// twice, under two ids, and both look perfectly valid. The endpoint Idempotency-Key filter
    /// cannot see this, because the replay happens wholly inside one HTTP call.
    /// </para>
    /// <para>
    /// Nullable: every movement recorded before this column existed has no answer and none can be
    /// invented. The index is filtered for that reason, and UNIQUE so that a replay which somehow
    /// got past the check-then-insert fails loudly instead of moving the money twice.
    /// </para>
    /// </summary>
    public partial class CapitalMovementAttemptKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "CapitalTransactions",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalTransactions_IdempotencyKey",
                table: "CapitalTransactions",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CapitalTransactions_IdempotencyKey",
                table: "CapitalTransactions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "CapitalTransactions");
        }
    }
}
