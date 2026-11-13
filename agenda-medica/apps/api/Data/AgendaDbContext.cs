using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Data;

public sealed class AgendaDbContext(DbContextOptions<AgendaDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMembership> OrganizationMemberships =>
        Set<OrganizationMembership>();

    public DbSet<OrganizationInvitation> OrganizationInvitations =>
        Set<OrganizationInvitation>();

    public DbSet<ScheduleRule> ScheduleRules => Set<ScheduleRule>();

    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<AppointmentAuditEvent> AppointmentAuditEvents => Set<AppointmentAuditEvent>();

    public DbSet<PublicBookingLink> PublicBookingLinks => Set<PublicBookingLink>();

    public DbSet<SecretaryPractitionerAssignment> SecretaryPractitionerAssignments =>
        Set<SecretaryPractitionerAssignment>();

    public DbSet<SecretaryAgendaAssignmentAuditEvent> SecretaryAgendaAssignmentAuditEvents =>
        Set<SecretaryAgendaAssignmentAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var organization = modelBuilder.Entity<Organization>();

        organization.ToTable(
            "Organizations",
            table => table.HasCheckConstraint(
                "CK_Organizations_AppointmentConfirmationMode",
                "[AppointmentConfirmationMode] IN ('ManualApproval', 'Automatic')"));
        organization.HasKey(item => item.Id);
        organization.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");
        organization.Property(item => item.Name)
            .HasMaxLength(120)
            .IsRequired();
        organization.Property(item => item.CountryCode)
            .HasMaxLength(2)
            .HasDefaultValue("GT")
            .IsRequired();
        organization.Property(item => item.TimeZoneId)
            .HasMaxLength(64)
            .HasDefaultValue("America/Guatemala")
            .IsRequired();
        organization.Property(item => item.AppointmentConfirmationMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(AppointmentConfirmationMode.ManualApproval)
            .IsRequired();

        var membership = modelBuilder.Entity<OrganizationMembership>();
        membership.ToTable(
            "OrganizationMemberships",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_OrganizationMemberships_Role",
                    "[Role] IN ('ClinicAdmin', 'Practitioner', 'Secretary')");
                table.HasCheckConstraint(
                    "CK_OrganizationMemberships_AppointmentDuration",
                    "[AppointmentDurationMinutes] IN (15, 20, 30, 45, 60)");
                table.HasCheckConstraint(
                    "CK_OrganizationMemberships_PreparationBuffer",
                    "[PreparationBufferMinutes] IN (0, 5, 10, 15)");
            });
        membership.HasKey(item => new { item.OrganizationId, item.UserId });
        membership.Property(item => item.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        membership.Property(item => item.AppointmentDurationMinutes)
            .HasDefaultValue(30)
            .IsRequired();
        membership.Property(item => item.PreparationBufferMinutes)
            .HasDefaultValue(5)
            .IsRequired();
        membership.HasOne(item => item.Organization)
            .WithMany(item => item.Memberships)
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        membership.HasOne(item => item.User)
            .WithMany(item => item.OrganizationMemberships)
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var invitation = modelBuilder.Entity<OrganizationInvitation>();
        invitation.ToTable(
            "OrganizationInvitations",
            table => table.HasCheckConstraint(
                "CK_OrganizationInvitations_Role",
                "[Role] IN ('Practitioner', 'Secretary')"));
        invitation.HasKey(item => item.Id);
        invitation.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");
        invitation.Property(item => item.Email)
            .HasMaxLength(254)
            .IsRequired();
        invitation.Property(item => item.EmailNormalized)
            .HasMaxLength(254)
            .IsRequired();
        invitation.Property(item => item.TokenHash)
            .HasColumnType("binary(32)")
            .IsRequired();
        invitation.HasIndex(item => item.TokenHash)
            .IsUnique();
        invitation.Property(item => item.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        invitation.Property(item => item.CreatedAt)
            .IsRequired();
        invitation.Property(item => item.ExpiresAt)
            .IsRequired();
        invitation.Property(item => item.Version)
            .IsRowVersion();
        invitation.HasIndex(item => new { item.OrganizationId, item.EmailNormalized })
            .IsUnique()
            .HasFilter("[AcceptedAt] IS NULL AND [RevokedAt] IS NULL");
        invitation.HasOne(item => item.Organization)
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        invitation.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        var scheduleRule = modelBuilder.Entity<ScheduleRule>();
        scheduleRule.ToTable(
            "ScheduleRules",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_ScheduleRules_DayOfWeek",
                    "[DayOfWeek] BETWEEN 0 AND 6");
                table.HasCheckConstraint(
                    "CK_ScheduleRules_TimeRange",
                    "[StartTime] < [EndTime]");
            });
        scheduleRule.HasKey(item => new
        {
            item.OrganizationId,
            item.PractitionerUserId,
            item.DayOfWeek,
            item.StartTime,
        });
        scheduleRule.Property(item => item.DayOfWeek)
            .HasConversion<int>();
        scheduleRule.Property(item => item.StartTime)
            .HasColumnType("time(0)");
        scheduleRule.Property(item => item.EndTime)
            .HasColumnType("time(0)")
            .IsRequired();
        scheduleRule.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.PractitionerUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        var scheduleException = modelBuilder.Entity<ScheduleException>();
        scheduleException.ToTable("ScheduleExceptions");
        scheduleException.HasKey(item => new
        {
            item.OrganizationId,
            item.PractitionerUserId,
            item.LocalDate,
        });
        scheduleException.Property(item => item.LocalDate)
            .HasColumnType("date");
        scheduleException.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.PractitionerUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        var appointment = modelBuilder.Entity<Appointment>();
        appointment.ToTable(
            "Appointments",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Appointments_Status",
                    "[Status] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow')");
                table.HasCheckConstraint(
                    "CK_Appointments_TimeRange",
                    "[StartsAtUtc] < [EndsAtUtc] AND [EndsAtUtc] <= [OccupiedUntilUtc]");
                table.HasCheckConstraint(
                    "CK_Appointments_Contact",
                    "([ContactEmail] IS NOT NULL OR [ContactPhone] IS NOT NULL) AND [ContactConsent] = 1");
                table.HasCheckConstraint(
                    "CK_Appointments_Duration",
                    "[DurationMinutes] IN (15, 20, 30, 45, 60)");
                table.HasCheckConstraint(
                    "CK_Appointments_PreparationBuffer",
                    "[PreparationBufferMinutes] IN (0, 5, 10, 15)");
            });
        appointment.HasKey(item => item.Id);
        appointment.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");
        appointment.Property(item => item.IdempotencyKey)
            .IsRequired();
        appointment.Property(item => item.PatientName)
            .HasMaxLength(120)
            .IsRequired();
        appointment.Property(item => item.ContactEmail)
            .HasMaxLength(254);
        appointment.Property(item => item.ContactPhone)
            .HasMaxLength(32);
        appointment.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        appointment.Property(item => item.Version)
            .IsRowVersion();
        appointment.HasIndex(item => new
            {
                item.OrganizationId,
                item.PractitionerUserId,
                item.IdempotencyKey,
            })
            .IsUnique();
        appointment.HasIndex(item => new
            {
                item.OrganizationId,
                item.PractitionerUserId,
                item.StartsAtUtc,
                item.OccupiedUntilUtc,
            })
            .HasFilter("[Status] IN ('Requested', 'Confirmed')");
        appointment.HasAlternateKey(item => new { item.OrganizationId, item.Id });
        appointment.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.PractitionerUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        var appointmentAudit = modelBuilder.Entity<AppointmentAuditEvent>();
        appointmentAudit.ToTable(
            "AppointmentAuditEvents",
            table => table.HasCheckConstraint(
                "CK_AppointmentAuditEvents_Status",
                "[NewStatus] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow') AND " +
                    "([PreviousStatus] IS NULL OR [PreviousStatus] IN ('Requested', 'Confirmed', 'Rejected', 'Cancelled', 'Completed', 'NoShow'))"));
        appointmentAudit.HasKey(item => item.Id);
        appointmentAudit.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");
        appointmentAudit.Property(item => item.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(32);
        appointmentAudit.Property(item => item.NewStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        appointmentAudit.HasIndex(item => new
        {
            item.OrganizationId,
            item.AppointmentId,
            item.ChangedAtUtc,
        });
        appointmentAudit.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, item.AppointmentId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        appointmentAudit.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.ActorUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        var publicBookingLink = modelBuilder.Entity<PublicBookingLink>();
        publicBookingLink.ToTable("PublicBookingLinks");
        publicBookingLink.HasKey(item => new
        {
            item.OrganizationId,
            item.PractitionerUserId,
        });
        publicBookingLink.Property(item => item.TokenHash)
            .HasColumnType("binary(32)")
            .IsRequired();
        publicBookingLink.Property(item => item.Version)
            .IsRowVersion();
        publicBookingLink.HasIndex(item => item.TokenHash)
            .IsUnique();
        publicBookingLink.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.PractitionerUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        var secretaryAssignment = modelBuilder.Entity<SecretaryPractitionerAssignment>();
        secretaryAssignment.ToTable("SecretaryPractitionerAssignments");
        secretaryAssignment.HasKey(item => new
        {
            item.OrganizationId,
            item.SecretaryUserId,
            item.PractitionerUserId,
        });
        secretaryAssignment.HasIndex(item => new
        {
            item.OrganizationId,
            item.PractitionerUserId,
        });
        secretaryAssignment.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.SecretaryUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        secretaryAssignment.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.PractitionerUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        var secretaryAssignmentAudit =
            modelBuilder.Entity<SecretaryAgendaAssignmentAuditEvent>();
        secretaryAssignmentAudit.ToTable(
            "SecretaryAgendaAssignmentAuditEvents",
            table => table.HasCheckConstraint(
                "CK_SecretaryAgendaAssignmentAuditEvents_Change",
                "[Change] IN ('Granted', 'Revoked')"));
        secretaryAssignmentAudit.HasKey(item => item.Id);
        secretaryAssignmentAudit.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()");
        secretaryAssignmentAudit.Property(item => item.Change)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        secretaryAssignmentAudit.HasIndex(item => new
        {
            item.OrganizationId,
            item.SecretaryUserId,
            item.ChangedAtUtc,
        });
        secretaryAssignmentAudit.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        secretaryAssignmentAudit.HasOne<OrganizationMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.OrganizationId, UserId = item.ActorUserId })
            .HasPrincipalKey(item => new { item.OrganizationId, item.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
