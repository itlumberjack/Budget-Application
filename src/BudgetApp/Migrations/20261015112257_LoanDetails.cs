using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class LoanDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "RecurringDebts",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BalanceAsOf",
                table: "RecurringDebts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestRate",
                table: "RecurringDebts",
                type: "decimal(6,3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayoffPriority",
                table: "RecurringDebts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DebtPlanSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Strategy = table.Column<int>(type: "int", nullable: false),
                    ExtraMonthly = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    OneTimeAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    OneTimeMonth = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtPlanSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "DebtPlanSettings",
                columns: new[] { "Id", "ExtraMonthly", "OneTimeAmount", "OneTimeMonth", "Strategy" },
                values: new object[] { 1, 0m, 0m, null, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtPlanSettings");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "BalanceAsOf",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "InterestRate",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "PayoffPriority",
                table: "RecurringDebts");
        }
    }
}
