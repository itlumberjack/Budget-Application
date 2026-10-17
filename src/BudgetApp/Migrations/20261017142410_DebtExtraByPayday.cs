using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class DebtExtraByPayday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtraForDebtId",
                table: "BudgetExpenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetExpenses_ExtraForDebtId",
                table: "BudgetExpenses",
                column: "ExtraForDebtId");

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetExpenses_RecurringDebts_ExtraForDebtId",
                table: "BudgetExpenses",
                column: "ExtraForDebtId",
                principalTable: "RecurringDebts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BudgetExpenses_RecurringDebts_ExtraForDebtId",
                table: "BudgetExpenses");

            migrationBuilder.DropIndex(
                name: "IX_BudgetExpenses_ExtraForDebtId",
                table: "BudgetExpenses");

            migrationBuilder.DropColumn(
                name: "ExtraForDebtId",
                table: "BudgetExpenses");
        }
    }
}
