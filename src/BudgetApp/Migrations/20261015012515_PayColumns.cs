using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class PayColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "RecurringDebts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardNextMonth",
                table: "IncomeSources",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "BudgetExpenses",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayPeriodId",
                table: "BudgetExpenses",
                type: "int",
                nullable: true);

            // Category names are unique, and the new names reuse old ones in different rows
            // (e.g. "Insurance" moves from row 4 to row 3), so clear them out of the way first.
            migrationBuilder.Sql("UPDATE Categories SET Name = CONCAT('tmp-', Id) WHERE Id BETWEEN 3 AND 9;");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Insurance");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Loans");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Investments");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Child Care");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "Subscriptions");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Household Needs");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Additional Deductions");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetExpenses_PayPeriodId",
                table: "BudgetExpenses",
                column: "PayPeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetExpenses_PayPeriods_PayPeriodId",
                table: "BudgetExpenses",
                column: "PayPeriodId",
                principalTable: "PayPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BudgetExpenses_PayPeriods_PayPeriodId",
                table: "BudgetExpenses");

            migrationBuilder.DropIndex(
                name: "IX_BudgetExpenses_PayPeriodId",
                table: "BudgetExpenses");

            migrationBuilder.DropColumn(
                name: "Company",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "CountsTowardNextMonth",
                table: "IncomeSources");

            migrationBuilder.DropColumn(
                name: "Company",
                table: "BudgetExpenses");

            migrationBuilder.DropColumn(
                name: "PayPeriodId",
                table: "BudgetExpenses");

            migrationBuilder.Sql("UPDATE Categories SET Name = CONCAT('tmp-', Id) WHERE Id BETWEEN 3 AND 9;");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Transportation");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Insurance");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Debt");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Groceries & Household");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "Kids");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Subscriptions");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Savings");
        }
    }
}
