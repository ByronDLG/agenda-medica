using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicBookingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicBookingLinks",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PractitionerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicBookingLinks", x => new { x.OrganizationId, x.PractitionerUserId });
                    table.ForeignKey(
                        name: "FK_PublicBookingLinks_OrganizationMemberships_OrganizationId_PractitionerUserId",
                        columns: x => new { x.OrganizationId, x.PractitionerUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookingLinks_TokenHash",
                table: "PublicBookingLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicBookingLinks");
        }
    }
}
