using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class BillKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "RecurringDebts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Anything that already has an amount owed or an interest rate is a loan; everything else starts as a bill.
            migrationBuilder.Sql("UPDATE `RecurringDebts` SET `Kind` = 2 WHERE `Balance` > 0 OR `InterestRate` > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RecurringDebts");
        }
    }
}
