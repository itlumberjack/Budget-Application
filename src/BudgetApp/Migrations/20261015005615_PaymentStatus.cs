using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class PaymentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "BudgetExpenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Items already checked as paid become Complete.
            migrationBuilder.Sql("UPDATE BudgetExpenses SET Status = 2 WHERE IsPaid = 1;");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "BudgetExpenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "BudgetExpenses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE BudgetExpenses SET IsPaid = 1 WHERE Status = 2;");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "BudgetExpenses");
        }
    }
}
