using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvitationConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationInvitations_OrganizationId_EmailNormalized_AcceptedAt_RevokedAt",
                table: "OrganizationInvitations");

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "OrganizationInvitations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_EmailNormalized",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "EmailNormalized" },
                unique: true,
                filter: "[AcceptedAt] IS NULL AND [RevokedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationInvitations_OrganizationId_EmailNormalized",
                table: "OrganizationInvitations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "OrganizationInvitations");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_EmailNormalized_AcceptedAt_RevokedAt",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "EmailNormalized", "AcceptedAt", "RevokedAt" });
        }
    }
}
