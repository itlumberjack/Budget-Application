using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class MonthEndBusinessDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MonthEndOnBusinessDay",
                table: "IncomeSources",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true); // existing paychecks move off weekends too
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthEndOnBusinessDay",
                table: "IncomeSources");
        }
    }
}
