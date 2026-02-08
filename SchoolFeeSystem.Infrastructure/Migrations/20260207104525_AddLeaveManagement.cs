using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolFeeSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaveDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaveType = table.Column<string>(type: "TEXT", nullable: false),
                    LeaveHours = table.Column<decimal>(type: "TEXT", nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<string>(type: "TEXT", nullable: false),
                    LeaveSource = table.Column<string>(type: "TEXT", nullable: false),
                    AllowanceMinutesUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    GrantedBy = table.Column<string>(type: "TEXT", nullable: false),
                    GrantedOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Salaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    BasicSalary = table.Column<decimal>(type: "TEXT", nullable: false),
                    HRA = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConveyanceAllowance = table.Column<decimal>(type: "TEXT", nullable: false),
                    MedicalAllowance = table.Column<decimal>(type: "TEXT", nullable: false),
                    OtherAllowances = table.Column<decimal>(type: "TEXT", nullable: false),
                    PFDeduction = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProfessionalTax = table.Column<decimal>(type: "TEXT", nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Salaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Salaries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_EmployeeId",
                table: "LeaveRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Salaries_EmployeeId",
                table: "Salaries",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "Salaries");
        }
    }
}
