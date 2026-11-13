using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => new { x.OrganizationId, x.PractitionerUserId, x.LocalDate });
                    table.ForeignKey(
                        name: "FK_ScheduleExceptions_OrganizationMemberships_OrganizationId_PractitionerUserId",
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
                name: "ScheduleExceptions");
        }
    }
}
