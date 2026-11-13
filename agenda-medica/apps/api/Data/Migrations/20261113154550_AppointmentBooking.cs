using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ContactConsent = table.Column<bool>(type: "bit", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OccupiedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    PreparationBufferMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.UniqueConstraint("AK_Appointments_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Appointments_Contact", "([ContactEmail] IS NOT NULL OR [ContactPhone] IS NOT NULL) AND [ContactConsent] = 1");
                    table.CheckConstraint("CK_Appointments_Duration", "[DurationMinutes] IN (15, 20, 30, 45, 60)");
                    table.CheckConstraint("CK_Appointments_PreparationBuffer", "[PreparationBufferMinutes] IN (0, 5, 10, 15)");
                    table.CheckConstraint("CK_Appointments_Status", "[Status] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow')");
                    table.CheckConstraint("CK_Appointments_TimeRange", "[StartsAtUtc] < [EndsAtUtc] AND [EndsAtUtc] <= [OccupiedUntilUtc]");
                    table.ForeignKey(
                        name: "FK_Appointments_OrganizationMemberships_OrganizationId_PractitionerUserId",
                        columns: x => new { x.OrganizationId, x.PractitionerUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AppointmentAuditEvents_Status", "[NewStatus] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow') AND ([PreviousStatus] IS NULL OR [PreviousStatus] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow'))");
                    table.ForeignKey(
                        name: "FK_AppointmentAuditEvents_Appointments_OrganizationId_AppointmentId",
                        columns: x => new { x.OrganizationId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentAuditEvents_OrganizationMemberships_OrganizationId_ActorUserId",
                        columns: x => new { x.OrganizationId, x.ActorUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAuditEvents_OrganizationId_ActorUserId",
                table: "AppointmentAuditEvents",
                columns: new[] { "OrganizationId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAuditEvents_OrganizationId_AppointmentId_ChangedAtUtc",
                table: "AppointmentAuditEvents",
                columns: new[] { "OrganizationId", "AppointmentId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId_PractitionerUserId_IdempotencyKey",
                table: "Appointments",
                columns: new[] { "OrganizationId", "PractitionerUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId_PractitionerUserId_StartsAtUtc_OccupiedUntilUtc",
                table: "Appointments",
                columns: new[] { "OrganizationId", "PractitionerUserId", "StartsAtUtc", "OccupiedUntilUtc" },
                filter: "[Status] IN ('Requested', 'Confirmed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentAuditEvents");

            migrationBuilder.DropTable(
                name: "Appointments");
        }
    }
}
