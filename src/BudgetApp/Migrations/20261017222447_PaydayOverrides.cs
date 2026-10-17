using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace BudgetApp.Migrations
{
    /// <inheritdoc />
    public partial class PaydayOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaydayOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    IncomeSourceId = table.Column<int>(type: "int", nullable: false),
                    RegularDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PayDate = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaydayOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaydayOverrides_IncomeSources_IncomeSourceId",
                        column: x => x.IncomeSourceId,
                        principalTable: "IncomeSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PaydayOverrides_IncomeSourceId_RegularDate",
                table: "PaydayOverrides",
                columns: new[] { "IncomeSourceId", "RegularDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaydayOverrides");
        }
    }
}
