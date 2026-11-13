using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecretaryAgendaAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecretaryAgendaAssignmentAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretaryUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Change = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretaryAgendaAssignmentAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_SecretaryAgendaAssignmentAuditEvents_Change", "[Change] IN ('Granted', 'Revoked')");
                    table.ForeignKey(
                        name: "FK_SecretaryAgendaAssignmentAuditEvents_OrganizationMemberships_OrganizationId_ActorUserId",
                        columns: x => new { x.OrganizationId, x.ActorUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecretaryAgendaAssignmentAuditEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecretaryPractitionerAssignments",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretaryUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretaryPractitionerAssignments", x => new { x.OrganizationId, x.SecretaryUserId, x.PractitionerUserId });
                    table.ForeignKey(
                        name: "FK_SecretaryPractitionerAssignments_OrganizationMemberships_OrganizationId_PractitionerUserId",
                        columns: x => new { x.OrganizationId, x.PractitionerUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecretaryPractitionerAssignments_OrganizationMemberships_OrganizationId_SecretaryUserId",
                        columns: x => new { x.OrganizationId, x.SecretaryUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecretaryAgendaAssignmentAuditEvents_OrganizationId_ActorUserId",
                table: "SecretaryAgendaAssignmentAuditEvents",
                columns: new[] { "OrganizationId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretaryAgendaAssignmentAuditEvents_OrganizationId_SecretaryUserId_ChangedAtUtc",
                table: "SecretaryAgendaAssignmentAuditEvents",
                columns: new[] { "OrganizationId", "SecretaryUserId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretaryPractitionerAssignments_OrganizationId_PractitionerUserId",
                table: "SecretaryPractitionerAssignments",
                columns: new[] { "OrganizationId", "PractitionerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecretaryAgendaAssignmentAuditEvents");

            migrationBuilder.DropTable(
                name: "SecretaryPractitionerAssignments");
        }
    }
}
