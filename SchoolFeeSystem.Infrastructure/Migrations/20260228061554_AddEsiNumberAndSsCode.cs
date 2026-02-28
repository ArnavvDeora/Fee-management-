using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolFeeSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEsiNumberAndSsCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EsiNumber",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsCode",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EsiNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SsCode",
                table: "Employees");
        }
    }
}
