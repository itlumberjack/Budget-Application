using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class HouseholdSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseholdSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BufferPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FirstPartnerName = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    SecondPartnerName = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    FirstPartnerPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "HouseholdSettings",
                columns: new[] { "Id", "BufferPercent", "FirstPartnerName", "FirstPartnerPercent", "SecondPartnerName" },
                values: new object[] { 1, 20m, "Brad", 50m, "Jessica" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdSettings");
        }
    }
}
