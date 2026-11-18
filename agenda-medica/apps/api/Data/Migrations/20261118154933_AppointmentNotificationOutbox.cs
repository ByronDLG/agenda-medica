using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AppointmentAuditEvents_OrganizationId_Id",
                table: "AppointmentAuditEvents",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "AppointmentNotificationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuditEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentNotificationOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_AppointmentNotificationOutboxMessages_AttemptCount", "[AttemptCount] BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_AppointmentNotificationOutboxMessages_Recipient", "[Recipient] IN ('Patient', 'Practitioner')");
                    table.CheckConstraint("CK_AppointmentNotificationOutboxMessages_Status", "[Status] IN ('Pending', 'Processing', 'Delivered', 'Failed')");
                    table.ForeignKey(
                        name: "FK_AppointmentNotificationOutboxMessages_AppointmentAuditEvents_OrganizationId_AuditEventId",
                        columns: x => new { x.OrganizationId, x.AuditEventId },
                        principalTable: "AppointmentAuditEvents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentNotificationOutboxMessages_Appointments_OrganizationId_AppointmentId",
                        columns: x => new { x.OrganizationId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_AuditEventId_Recipient",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "AuditEventId", "Recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_OrganizationId_AppointmentId",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "OrganizationId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_OrganizationId_AuditEventId",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "OrganizationId", "AuditEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_Status_NextAttemptAtUtc_CreatedAtUtc",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "Status", "NextAttemptAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentNotificationOutboxMessages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AppointmentAuditEvents_OrganizationId_Id",
                table: "AppointmentAuditEvents");
        }
    }
}
