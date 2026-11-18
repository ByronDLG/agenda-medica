using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgendaMedica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentNotificationOutboxMessages_AuditEventId_Recipient",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Status",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AppointmentNotificationOutboxMessages",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [AppointmentNotificationOutboxMessages] SET [Kind] = N'EventUpdate' WHERE [Kind] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "AppointmentNotificationOutboxMessages",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(24)",
                oldMaxLength: 24,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_AuditEventId_Kind_Recipient",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "AuditEventId", "Kind", "Recipient" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Kind",
                table: "AppointmentNotificationOutboxMessages",
                sql: "[Kind] IN ('EventUpdate', 'Reminder24Hours')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Status",
                table: "AppointmentNotificationOutboxMessages",
                sql: "[Status] IN ('Pending', 'Processing', 'Delivered', 'Failed', 'Skipped')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentNotificationOutboxMessages_AuditEventId_Kind_Recipient",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Kind",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Status",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AppointmentNotificationOutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentNotificationOutboxMessages_AuditEventId_Recipient",
                table: "AppointmentNotificationOutboxMessages",
                columns: new[] { "AuditEventId", "Recipient" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentNotificationOutboxMessages_Status",
                table: "AppointmentNotificationOutboxMessages",
                sql: "[Status] IN ('Pending', 'Processing', 'Delivered', 'Failed')");
        }
    }
}
