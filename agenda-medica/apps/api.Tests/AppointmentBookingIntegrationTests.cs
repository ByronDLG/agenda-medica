using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
}
