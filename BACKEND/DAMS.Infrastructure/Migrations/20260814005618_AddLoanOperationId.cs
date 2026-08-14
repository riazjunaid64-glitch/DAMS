using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanOperationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "LoanTransactions",
                type: "uniqueidentifier",
                nullable: true);

            // Safe even if the first loan migration was deployed and rows were entered before
            // this hardening migration reached the server: every historic row gets a distinct key.
            migrationBuilder.Sql("UPDATE [LoanTransactions] SET [OperationId] = NEWID() WHERE [OperationId] IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "OperationId",
                table: "LoanTransactions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanTransactions_OperationId",
                table: "LoanTransactions",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanTransactions_OperationId",
                table: "LoanTransactions");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "LoanTransactions");
        }
    }
}
