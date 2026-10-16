using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class FixedBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFixed",
                table: "RecurringDebts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFixed",
                table: "BudgetExpenses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Weekly and every-two-weeks bills start out fixed; month copies follow their bill.
            migrationBuilder.Sql("UPDATE RecurringDebts SET IsFixed = 1 WHERE Frequency <> 0;");
            migrationBuilder.Sql("UPDATE BudgetExpenses e JOIN RecurringDebts d ON d.Id = e.RecurringDebtId SET e.IsFixed = d.IsFixed;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFixed",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "IsFixed",
                table: "BudgetExpenses");
        }
    }
}
