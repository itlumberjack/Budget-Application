using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class LoanPaymentSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentFrequency",
                table: "DebtPlanSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentStartDate",
                table: "DebtPlanSettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "DebtPlanSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PaymentFrequency", "PaymentStartDate" },
                values: new object[] { 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentFrequency",
                table: "DebtPlanSettings");

            migrationBuilder.DropColumn(
                name: "PaymentStartDate",
                table: "DebtPlanSettings");
        }
    }
}
