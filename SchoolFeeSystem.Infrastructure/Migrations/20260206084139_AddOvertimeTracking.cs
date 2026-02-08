using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolFeeSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // =====================================================
            // STEP 1: Add new columns to existing AttendanceRecords table
            // =====================================================
            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "AttendanceRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LateMinutes",
                table: "AttendanceRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LatePenaltyMinutes",
                table: "AttendanceRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AllowanceTimeUsed",
                table: "AttendanceRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // =====================================================
            // STEP 2: Create new OvertimeAllowances table
            // =====================================================
            migrationBuilder.CreateTable(
                name: "OvertimeAllowances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAllowanceMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    UsedAllowanceMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeAllowances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeAllowances_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // =====================================================
            // STEP 3: Create indexes for performance
            // =====================================================
            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAllowances_EmployeeId",
                table: "OvertimeAllowances",
                column: "EmployeeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: Drop table
            migrationBuilder.DropTable(
                name: "OvertimeAllowances");

            // Rollback: Drop columns
            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "LateMinutes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "LatePenaltyMinutes",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "AllowanceTimeUsed",
                table: "AttendanceRecords");
        }
    }
}