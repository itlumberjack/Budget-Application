using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class BudgetPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PlannedIncome",
                table: "PayPeriods",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedSpending",
                table: "PayPeriods",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanSavedAt",
                table: "MonthlyBudgets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedAmount",
                table: "BudgetExpenses",
                type: "decimal(12,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannedIncome",
                table: "PayPeriods");

            migrationBuilder.DropColumn(
                name: "PlannedSpending",
                table: "PayPeriods");

            migrationBuilder.DropColumn(
                name: "PlanSavedAt",
                table: "MonthlyBudgets");

            migrationBuilder.DropColumn(
                name: "PlannedAmount",
                table: "BudgetExpenses");
        }
    }
}
