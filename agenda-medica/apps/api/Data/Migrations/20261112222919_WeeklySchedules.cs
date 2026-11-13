using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeeklySchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleRules",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRules", x => new { x.OrganizationId, x.PractitionerUserId, x.DayOfWeek, x.StartTime });
                    table.CheckConstraint("CK_ScheduleRules_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_ScheduleRules_TimeRange", "[StartTime] < [EndTime]");
                    table.ForeignKey(
                        name: "FK_ScheduleRules_OrganizationMemberships_OrganizationId_PractitionerUserId",
                        columns: x => new { x.OrganizationId, x.PractitionerUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleRules");
        }
    }
}
