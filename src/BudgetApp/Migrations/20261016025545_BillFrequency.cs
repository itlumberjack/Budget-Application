using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class BillFrequency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Frequency",
                table: "RecurringDebts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "RecurringDebts",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "RecurringDebts");
        }
    }
}
