using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolFeeSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlaggedBiometricEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlaggedBiometricEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BiometricId = table.Column<string>(type: "TEXT", nullable: false),
                    BiometricName = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFormat = table.Column<string>(type: "TEXT", nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedToEmployeeId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstSeenOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedOn = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlaggedBiometricEntries", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlaggedBiometricEntries");
        }
    }
}
