using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class LoanPromos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromoBalance",
                table: "RecurringDebts",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PromoDeferredInterest",
                table: "RecurringDebts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromoEndDate",
                table: "RecurringDebts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PromoRate",
                table: "RecurringDebts",
                type: "decimal(6,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromoBalance",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "PromoDeferredInterest",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "PromoEndDate",
                table: "RecurringDebts");

            migrationBuilder.DropColumn(
                name: "PromoRate",
                table: "RecurringDebts");
        }
    }
}
