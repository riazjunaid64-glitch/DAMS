using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <summary>
    /// Lets a customer payment recognise its own committed work when the write is replayed.
    /// <para>
    /// Recording a booking-amount or installment payment runs inside a retrying execution strategy.
    /// When a commit reaches the server but its acknowledgement does not come back, that is
    /// indistinguishable from a commit that never happened, so the strategy replays the whole
    /// delegate — and the delegate created the payment unconditionally, so the customer's money was
    /// banked twice under two receipt numbers with nothing on either row to connect them.
    /// </para>
    /// <para>
    /// The endpoint's <c>Idempotency-Key</c> filter does not reach this: it guards the HTTP call
    /// from outside, in a scope of its own, and this replay happens wholly inside one such call. So
    /// the attempt now names itself on the row it writes and looks for that name before writing.
    /// UNIQUE and filtered: every payment recorded before this column existed is NULL, and those
    /// cannot collide with each other.
    /// </para>
    /// </summary>
    public partial class AddPaymentAttemptKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Payments");
        }
    }
}
