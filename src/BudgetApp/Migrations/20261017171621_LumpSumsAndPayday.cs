using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class LumpSumsAndPayday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtraIncomeSourceId",
                table: "DebtPlanSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraPaydayNumber",
                table: "DebtPlanSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DebtLumpSums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Note = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtLumpSums", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "DebtPlanSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ExtraIncomeSourceId", "ExtraPaydayNumber" },
                values: new object[] { null, 0 });

            // The one-time extra saved before moves to the new list.
            migrationBuilder.Sql("INSERT INTO DebtLumpSums (Date, Amount) SELECT OneTimeMonth, OneTimeAmount FROM DebtPlanSettings WHERE OneTimeAmount > 0 AND OneTimeMonth IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "OneTimeAmount",
                table: "DebtPlanSettings");

            migrationBuilder.DropColumn(
                name: "OneTimeMonth",
                table: "DebtPlanSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtLumpSums");

            migrationBuilder.DropColumn(
                name: "ExtraIncomeSourceId",
                table: "DebtPlanSettings");

            migrationBuilder.DropColumn(
                name: "ExtraPaydayNumber",
                table: "DebtPlanSettings");

            migrationBuilder.AddColumn<decimal>(
                name: "OneTimeAmount",
                table: "DebtPlanSettings",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "OneTimeMonth",
                table: "DebtPlanSettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "DebtPlanSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "OneTimeAmount", "OneTimeMonth" },
                values: new object[] { 0m, null });
        }
    }
}
