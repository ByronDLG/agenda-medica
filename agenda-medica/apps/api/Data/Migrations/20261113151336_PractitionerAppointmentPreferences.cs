using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PractitionerAppointmentPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppointmentDurationMinutes",
                table: "OrganizationMemberships",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PreparationBufferMinutes",
                table: "OrganizationMemberships",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationMemberships_AppointmentDuration",
                table: "OrganizationMemberships",
                sql: "[AppointmentDurationMinutes] IN (15, 20, 30, 45, 60)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationMemberships_PreparationBuffer",
                table: "OrganizationMemberships",
                sql: "[PreparationBufferMinutes] IN (0, 5, 10, 15)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationMemberships_AppointmentDuration",
                table: "OrganizationMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationMemberships_PreparationBuffer",
                table: "OrganizationMemberships");

            migrationBuilder.DropColumn(
                name: "AppointmentDurationMinutes",
                table: "OrganizationMemberships");

            migrationBuilder.DropColumn(
                name: "PreparationBufferMinutes",
                table: "OrganizationMemberships");
        }
    }
}
