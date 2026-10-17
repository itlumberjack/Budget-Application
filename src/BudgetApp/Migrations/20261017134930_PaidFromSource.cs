using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class PaidFromSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaidFromIncomeSourceId",
                table: "RecurringDebts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringDebts_PaidFromIncomeSourceId",
                table: "RecurringDebts",
                column: "PaidFromIncomeSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringDebts_IncomeSources_PaidFromIncomeSourceId",
                table: "RecurringDebts",
                column: "PaidFromIncomeSourceId",
                principalTable: "IncomeSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringDebts_IncomeSources_PaidFromIncomeSourceId",
                table: "RecurringDebts");

            migrationBuilder.DropIndex(
                name: "IX_RecurringDebts_PaidFromIncomeSourceId",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "PaidFromIncomeSourceId",
                table: "RecurringDebts");
        }
    }
}
