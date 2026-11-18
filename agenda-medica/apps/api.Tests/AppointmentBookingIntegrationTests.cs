using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using AgendaMedica.Api.Infrastructure.Appointments;
using AgendaMedica.Api.Infrastructure.Email;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgendaMedica.Api.Tests;

public sealed class AppointmentBookingIntegrationTests
{
    [Fact]
    public async Task ConcurrentBookingsSerializeAndRetriesRemainIdempotent()
    {
        var databaseName = $"AgendaMedicaBookingTest_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
        }.ConnectionString;
        await CreateDatabaseAsync(masterConnectionString, databaseName);

        var connectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AgendaDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using (var database = new AgendaDbContext(options))
            {
                await database.Database.MigrateAsync();
                var migrator = database.GetService<IMigrator>();
                var migrations = database.Database.GetMigrations().ToArray();
                var appointmentMigration = Assert.Single(
                    migrations,
                    migration => migration.EndsWith(
                        "_AppointmentBooking",
                        StringComparison.Ordinal));
                var appointmentMigrationIndex = Array.IndexOf(migrations, appointmentMigration);
                Assert.True(appointmentMigrationIndex > 0);
                await migrator.MigrateAsync(migrations[appointmentMigrationIndex - 1]);
                await migrator.MigrateAsync(migrations[^1]);
            }

            var organization = Organization.Create("Consultorio de prueba");
            var practitioner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = $"practitioner-{Guid.NewGuid():N}@example.test",
                UserName = $"practitioner-{Guid.NewGuid():N}@example.test",
            };
            var otherPractitioner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"other-practitioner-{Guid.NewGuid():N}@example.test",
            };
            var secretary = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"secretary-{Guid.NewGuid():N}@example.test",
            };
            var administrator = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"administrator-{Guid.NewGuid():N}@example.test",
            };
            var otherOrganization = Organization.Create("Otro consultorio");
            var otherOrganizationPractitioner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"other-org-practitioner-{Guid.NewGuid():N}@example.test",
            };
            var changedRoleMember = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"changed-role-{Guid.NewGuid():N}@example.test",
            };
            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(
                    DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala"))
                    .DateTime).AddDays(3);
            var (publicToken, publicTokenHash) = PublicBookingToken.Create();
            Assert.True(PublicBookingToken.TryHash(publicToken, out var validatedTokenHash));
            Assert.Equal(publicTokenHash, validatedTokenHash);

            await using (var database = new AgendaDbContext(options))
            {
                database.Users.AddRange(
                    practitioner,
                    otherPractitioner,
                    secretary,
                    administrator,
                    otherOrganizationPractitioner,
                    changedRoleMember);
                database.Organizations.AddRange(organization, otherOrganization);
                await database.SaveChangesAsync();
                database.OrganizationMemberships.AddRange(
                    OrganizationMembership.Create(
                        practitioner,
                        organization,
                        OrganizationRole.Practitioner),
                    OrganizationMembership.Create(
                        otherPractitioner,
                        organization,
                        OrganizationRole.Practitioner),
                    OrganizationMembership.Create(
                        changedRoleMember,
                        organization,
                        OrganizationRole.Secretary),
                    OrganizationMembership.Create(
                        secretary,
                        organization,
                        OrganizationRole.Secretary),
                    OrganizationMembership.Create(
                        administrator,
                        organization,
                        OrganizationRole.ClinicAdmin),
                    OrganizationMembership.Create(
                        secretary,
                        otherOrganization,
                        OrganizationRole.Secretary),
                    OrganizationMembership.Create(
                        otherOrganizationPractitioner,
                        otherOrganization,
                        OrganizationRole.Practitioner));
                database.ScheduleRules.Add(ScheduleRule.Create(
                    organization.Id,
                    practitioner.Id,
                    localDate.DayOfWeek,
                    new TimeOnly(9, 0),
                    new TimeOnly(10, 15)));
                database.SecretaryPractitionerAssignments.Add(
                    SecretaryPractitionerAssignment.Create(
                        organization.Id,
                        secretary.Id,
                        practitioner.Id));
                database.SecretaryPractitionerAssignments.Add(
                    SecretaryPractitionerAssignment.Create(
                        organization.Id,
                        secretary.Id,
                        changedRoleMember.Id));
                database.SecretaryAgendaAssignmentAuditEvents.Add(
                    SecretaryAgendaAssignmentAuditEvent.Create(
                        organization.Id,
                        secretary.Id,
                        practitioner.Id,
                        administrator.Id,
                        SecretaryAgendaAssignmentChange.Granted,
                        DateTimeOffset.UtcNow));
                database.PublicBookingLinks.Add(PublicBookingLink.Create(
                    organization.Id,
                    practitioner.Id,
                    publicTokenHash,
                    DateTimeOffset.UtcNow));
                await database.SaveChangesAsync();
            }

            await using (var database = new AgendaDbContext(options))
            {
                var access = new PractitionerAgendaAccessService(database);
                Assert.True(await access.CanAccessPractitionerAgendaAsync(
                    organization.Id,
                    secretary.Id,
                    practitioner.Id,
                    CancellationToken.None));
                Assert.False(await access.CanAccessPractitionerAgendaAsync(
                    organization.Id,
                    secretary.Id,
                    otherPractitioner.Id,
                    CancellationToken.None));
                Assert.False(await access.CanAccessPractitionerAgendaAsync(
                    organization.Id,
                    secretary.Id,
                    changedRoleMember.Id,
                    CancellationToken.None));
                Assert.False(await access.CanAccessPractitionerAgendaAsync(
                    otherOrganization.Id,
                    secretary.Id,
                    otherOrganizationPractitioner.Id,
                    CancellationToken.None));
                Assert.Equal(
                    practitioner.Id,
                    Assert.Single(await access.GetAuthorizedAgendasAsync(
                        organization.Id,
                        secretary.Id,
                        CancellationToken.None)).UserId);
            }

            await using (var database = new AgendaDbContext(options))
            {
                var availability = await new PublicBookingAvailabilityService(
                    database,
                    TimeProvider.System).GetAsync(
                        publicTokenHash,
                        days: 14,
                        CancellationToken.None);
                Assert.NotNull(availability);
                var availableDay = Assert.Single(
                    availability.Days,
                    day => day.LocalDate == localDate.ToString("yyyy-MM-dd"));
                Assert.Contains(availableDay.Slots, slot => slot.StartTime == "09:00");
            }

            var secretaryBooking = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                Guid.NewGuid(),
                localDate,
                startTime: "09:35",
                actorUserId: secretary.Id,
                authorizedSecretaryUserId: secretary.Id);
            Assert.Equal(AppointmentBookingError.None, secretaryBooking.Error);
            Assert.NotNull(secretaryBooking.Appointment);
            var secretaryAppointmentId = secretaryBooking.Appointment.Id;

            var unassignedBooking = await BookAsync(
                options,
                organization.Id,
                otherPractitioner.Id,
                Guid.NewGuid(),
                localDate,
                actorUserId: secretary.Id,
                authorizedSecretaryUserId: secretary.Id);
            Assert.Equal(
                AppointmentBookingError.SecretaryNotAssigned,
                unassignedBooking.Error);

            await using (var database = new AgendaDbContext(options))
            {
                var audit = await database.AppointmentAuditEvents.SingleAsync(
                    item => item.AppointmentId == secretaryBooking.Appointment!.Id);
                Assert.Equal(secretary.Id, audit.ActorUserId);
                Assert.Equal(
                    3,
                    await database.AppointmentNotificationOutboxMessages.CountAsync(
                        item => item.AppointmentId == secretaryAppointmentId));
                Assert.Single(
                    await database.AppointmentNotificationOutboxMessages
                        .Where(item =>
                            item.AppointmentId == secretaryAppointmentId &&
                            item.Kind == AppointmentNotificationKind.Reminder24Hours)
                        .ToArrayAsync());
            }

            var emailSender = new RecordingEmailSender();
            await using (var database = new AgendaDbContext(options))
            {
                var processor = new AppointmentNotificationProcessor(
                    database,
                    emailSender,
                    TimeProvider.System,
                    NullLogger<AppointmentNotificationProcessor>.Instance);
                Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
                Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
                Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
            }

            Assert.Equal(2, emailSender.Messages.Count);
            Assert.Contains(
                emailSender.Messages,
                message => message.RecipientAddress == "persona@example.test" &&
                    message.TextBody.Contains("Persona de prueba", StringComparison.Ordinal));
            Assert.Contains(
                emailSender.Messages,
                message => message.RecipientAddress == practitioner.Email &&
                    !message.TextBody.Contains("Persona de prueba", StringComparison.Ordinal));
            Assert.All(
                emailSender.Messages,
                message => Assert.Contains("@agenda-medica.local", message.MessageId!));

            await using (var database = new AgendaDbContext(options))
            {
                var deliveredMessages = await database.AppointmentNotificationOutboxMessages
                    .Where(item =>
                        item.AppointmentId == secretaryAppointmentId &&
                        item.Kind == AppointmentNotificationKind.EventUpdate &&
                        item.Status == AppointmentNotificationDeliveryStatus.Delivered)
                    .ToArrayAsync();
                Assert.Equal(2, deliveredMessages.Length);
                Assert.All(
                    deliveredMessages,
                    message => Assert.Empty(message.RecipientAddress));
            }

            var failedBooking = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                Guid.NewGuid(),
                localDate.AddDays(7),
                startTime: "09:00");
            Assert.Equal(AppointmentBookingError.None, failedBooking.Error);
            Assert.NotNull(failedBooking.Appointment);
            var failedAppointmentId = failedBooking.Appointment.Id;
            var failingEmailSender = new RecordingEmailSender(failDelivery: true);
            await using (var database = new AgendaDbContext(options))
            {
                var processor = new AppointmentNotificationProcessor(
                    database,
                    failingEmailSender,
                    TimeProvider.System,
                    NullLogger<AppointmentNotificationProcessor>.Instance);
                Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
            }

            await using (var database = new AgendaDbContext(options))
            {
                var retryingMessage = await database.AppointmentNotificationOutboxMessages
                    .SingleAsync(item =>
                        item.AppointmentId == failedAppointmentId &&
                        item.AttemptCount == 1);
                Assert.Equal(AppointmentNotificationDeliveryStatus.Pending, retryingMessage.Status);
                Assert.Equal("provider-failure", retryingMessage.LastFailureCode);
                Assert.True(retryingMessage.NextAttemptAtUtc > DateTimeOffset.UtcNow);
                Assert.True(await database.Appointments.AnyAsync(
                    item => item.Id == failedAppointmentId));
            }

            DateTimeOffset secretaryReminderAtUtc;
            Guid secretaryReminderMessageId;
            await using (var database = new AgendaDbContext(options))
            {
                var reminder = await database.AppointmentNotificationOutboxMessages
                    .SingleAsync(item =>
                        item.AppointmentId == secretaryAppointmentId &&
                        item.Kind == AppointmentNotificationKind.Reminder24Hours);
                secretaryReminderAtUtc = reminder.NextAttemptAtUtc;
                secretaryReminderMessageId = reminder.Id;
            }

            var reminderEmailSender = new RecordingEmailSender();
            await using (var database = new AgendaDbContext(options))
            {
                var processor = new AppointmentNotificationProcessor(
                    database,
                    reminderEmailSender,
                    new FixedTimeProvider(secretaryReminderAtUtc.AddMinutes(1)),
                    NullLogger<AppointmentNotificationProcessor>.Instance);
                var processedCount = 0;
                while (await processor.ProcessNextAsync(CancellationToken.None))
                {
                    processedCount++;
                    Assert.True(processedCount <= 10);
                }
            }

            Assert.Contains(
                reminderEmailSender.Messages,
                message => message.MessageId == $"{secretaryReminderMessageId:N}@agenda-medica.local" &&
                    message.Subject == "Recordatorio de tu cita");
            await using (var database = new AgendaDbContext(options))
            {
                var sentReminder = await database.AppointmentNotificationOutboxMessages
                    .SingleAsync(item => item.Id == secretaryReminderMessageId);
                Assert.Equal(AppointmentNotificationDeliveryStatus.Delivered, sentReminder.Status);
            }

            DateTimeOffset failedReminderAtUtc;
            Guid failedReminderMessageId;
            await using (var database = new AgendaDbContext(options))
            {
                var appointment = await database.Appointments.SingleAsync(
                    item => item.Id == failedAppointmentId);
                appointment.ChangeStatus(AppointmentStatus.Cancelled);
                var changedAtUtc = DateTimeOffset.UtcNow;
                var cancellationEvent = AppointmentAuditEvent.Create(
                    organization.Id,
                    appointment.Id,
                    practitioner.Id,
                    AppointmentStatus.Requested,
                    AppointmentStatus.Cancelled,
                    changedAtUtc);
                database.AppointmentAuditEvents.Add(cancellationEvent);
                database.AppointmentNotificationOutboxMessages.AddRange(
                    AppointmentNotificationOutboxMessage.CreateForEvent(
                        appointment,
                        cancellationEvent,
                        practitioner.Email,
                        changedAtUtc));
                await database.SaveChangesAsync();

                var reminder = await database.AppointmentNotificationOutboxMessages
                    .SingleAsync(item =>
                        item.AppointmentId == failedAppointmentId &&
                        item.Kind == AppointmentNotificationKind.Reminder24Hours);
                failedReminderAtUtc = reminder.NextAttemptAtUtc;
                failedReminderMessageId = reminder.Id;
            }

            var delayedEmailSender = new RecordingEmailSender();
            await using (var database = new AgendaDbContext(options))
            {
                var processor = new AppointmentNotificationProcessor(
                    database,
                    delayedEmailSender,
                    new FixedTimeProvider(failedReminderAtUtc.AddMinutes(1)),
                    NullLogger<AppointmentNotificationProcessor>.Instance);
                var processedCount = 0;
                while (await processor.ProcessNextAsync(CancellationToken.None))
                {
                    processedCount++;
                    Assert.True(processedCount <= 10);
                }
            }

            Assert.DoesNotContain(
                delayedEmailSender.Messages,
                message => message.MessageId == $"{failedReminderMessageId:N}@agenda-medica.local");
            await using (var database = new AgendaDbContext(options))
            {
                var skippedReminder = await database.AppointmentNotificationOutboxMessages
                    .SingleAsync(item => item.Id == failedReminderMessageId);
                Assert.Equal(AppointmentNotificationDeliveryStatus.Skipped, skippedReminder.Status);
                Assert.Equal("appointment-inactive", skippedReminder.LastFailureCode);
                Assert.Empty(skippedReminder.RecipientAddress);
            }

            var firstKey = Guid.NewGuid();
            var secondKey = Guid.NewGuid();
            var firstRequest = BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                firstKey,
                localDate,
                publicBookingTokenHash: publicTokenHash);
            var secondRequest = BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                secondKey,
                localDate,
                publicBookingTokenHash: publicTokenHash);
            var results = await Task.WhenAll(firstRequest, secondRequest);

            Assert.Single(results, result => result.Error == AppointmentBookingError.None);
            Assert.Single(results, result => result.Error == AppointmentBookingError.SlotUnavailable);
            var winningKey = results[0].Error == AppointmentBookingError.None
                ? firstKey
                : secondKey;
            var winningAppointment = Assert.Single(
                results.Where(result => result.Error == AppointmentBookingError.None)
                    .Select(result => result.Appointment));
            Assert.NotNull(winningAppointment);

            var retry = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                winningKey,
                localDate,
                publicBookingTokenHash: publicTokenHash);
            Assert.Equal(AppointmentBookingError.None, retry.Error);
            Assert.Equal(winningAppointment.Id, retry.Appointment?.Id);

            var reusedKey = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                winningKey,
                localDate,
                patientName: "Otro nombre",
                publicBookingTokenHash: publicTokenHash);
            Assert.Equal(AppointmentBookingError.IdempotencyKeyReused, reusedKey.Error);

            await using (var database = new AgendaDbContext(options))
            {
                var appointment = await database.Appointments.SingleAsync(
                    item => item.Id == winningAppointment.Id);
                appointment.ChangeStatus(AppointmentStatus.Cancelled);
                await database.SaveChangesAsync();
            }

            await using (var database = new AgendaDbContext(options))
            {
                var availability = await new PublicBookingAvailabilityService(
                    database,
                    TimeProvider.System).GetAsync(
                        publicTokenHash,
                        days: 14,
                        CancellationToken.None);
                Assert.NotNull(availability);
                var availableDay = Assert.Single(
                    availability.Days,
                    day => day.LocalDate == localDate.ToString("yyyy-MM-dd"));
                Assert.Contains(availableDay.Slots, slot => slot.StartTime == "09:00");
            }

            var afterCancellation = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                Guid.NewGuid(),
                localDate,
                publicBookingTokenHash: publicTokenHash);
            Assert.Equal(AppointmentBookingError.None, afterCancellation.Error);

            var unauthorizedPractitioner = await BookAsync(
                options,
                organization.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                localDate,
                publicBookingTokenHash: publicTokenHash);
            Assert.Equal(
                AppointmentBookingError.NotPractitioner,
                unauthorizedPractitioner.Error);

            await using (var database = new AgendaDbContext(options))
            {
                var link = await database.PublicBookingLinks.SingleAsync();
                database.PublicBookingLinks.Remove(link);
                await database.SaveChangesAsync();
            }

            await using (var database = new AgendaDbContext(options))
            {
                var availability = await new PublicBookingAvailabilityService(
                    database,
                    TimeProvider.System).GetBookingContextAsync(
                        publicTokenHash,
                        CancellationToken.None);
                Assert.Null(availability);
            }

            var revokedLinkBooking = await BookAsync(
                options,
                organization.Id,
                practitioner.Id,
                Guid.NewGuid(),
                localDate,
                publicBookingTokenHash: publicTokenHash);
            Assert.Equal(AppointmentBookingError.PublicLinkNotFound, revokedLinkBooking.Error);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static async Task<AppointmentBookingResult> BookAsync(
        DbContextOptions<AgendaDbContext> options,
        Guid organizationId,
        Guid practitionerId,
        Guid idempotencyKey,
        DateOnly localDate,
        string patientName = "Persona de prueba",
        byte[]? publicBookingTokenHash = null,
        string startTime = "09:00",
        Guid? actorUserId = null,
        Guid? authorizedSecretaryUserId = null)
    {
        await using var database = new AgendaDbContext(options);
        var service = new AppointmentBookingService(database, TimeProvider.System);
        return await service.BookAsync(
            organizationId,
            practitionerId,
            idempotencyKey,
            localDate,
            startTime,
            patientName,
            "persona@example.test",
            null,
            contactConsent: true,
            CancellationToken.None,
            requiredPublicBookingTokenHash: publicBookingTokenHash,
            actorUserId: actorUserId,
            authorizedSecretaryUserId: authorizedSecretaryUserId);
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly bool failDelivery;

        public RecordingEmailSender(bool failDelivery = false)
        {
            this.failDelivery = failDelivery;
        }

        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            if (failDelivery)
            {
                throw new EmailDeliveryException();
            }

            return Task.CompletedTask;
        }

    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
