using System.Data;
using System.Globalization;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public sealed class AppointmentBookingService(
    AgendaDbContext database,
    TimeProvider timeProvider)
{
    private const string TimeFormat = "HH:mm";
    private const int MaximumBookingDaysAhead = 365;

    public async Task<AppointmentBookingResult> BookAsync(
        Guid organizationId,
        Guid practitionerUserId,
        Guid idempotencyKey,
        DateOnly localDate,
        string startTime,
        string patientName,
        string? contactEmail,
        string? contactPhone,
        bool contactConsent,
        CancellationToken cancellationToken,
        byte[]? requiredPublicBookingTokenHash = null,
        Guid? actorUserId = null,
        Guid? authorizedSecretaryUserId = null)
    {
        if (idempotencyKey == Guid.Empty ||
            localDate == default ||
            string.IsNullOrWhiteSpace(patientName) ||
            (string.IsNullOrWhiteSpace(contactEmail) && string.IsNullOrWhiteSpace(contactPhone)) ||
            !contactConsent)
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.InvalidRequest);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            practitionerUserId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.NotPractitioner);
        }

        if (authorizedSecretaryUserId is Guid secretaryUserId &&
            !await database.SecretaryPractitionerAssignments.AnyAsync(
                item => item.OrganizationId == organizationId &&
                    item.SecretaryUserId == secretaryUserId &&
                    item.PractitionerUserId == practitionerUserId,
                cancellationToken))
        {
            return AppointmentBookingResult.Failed(
                AppointmentBookingError.SecretaryNotAssigned);
        }

        if (requiredPublicBookingTokenHash is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(
                requiredPublicBookingTokenHash.Length,
                32);
            var link = await database.PublicBookingLinks
                .FromSqlInterpolated(
                    $"SELECT * FROM [PublicBookingLinks] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [PractitionerUserId] = {practitionerUserId} AND [TokenHash] = {requiredPublicBookingTokenHash}")
                .SingleOrDefaultAsync(cancellationToken);
            if (link is null)
            {
                return AppointmentBookingResult.Failed(AppointmentBookingError.PublicLinkNotFound);
            }
        }

        var organization = await database.Organizations
            .SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization is null)
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.OrganizationNotFound);
        }

        if (!TimeOnly.TryParseExact(
                startTime,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localStartTime))
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.InvalidLocalTime);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(organization.TimeZoneId);
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
        if (localDate > localToday.AddDays(MaximumBookingDaysAhead))
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.InvalidRequest);
        }

        var localStart = localDate.ToDateTime(localStartTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localStart) || timeZone.IsAmbiguousTime(localStart))
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.InvalidLocalTime);
        }

        var startsAtUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
        var existingRequest = await database.Appointments
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId &&
                    item.PractitionerUserId == practitionerUserId &&
                    item.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existingRequest is not null)
        {
            if (!existingRequest.HasSameRequest(
                    startsAtUtc,
                    patientName,
                    contactEmail,
                    contactPhone,
                    contactConsent))
            {
                return AppointmentBookingResult.Failed(
                    AppointmentBookingError.IdempotencyKeyReused);
            }

            await transaction.CommitAsync(cancellationToken);
            return AppointmentBookingResult.Succeeded(existingRequest);
        }

        if (startsAtUtc <= timeProvider.GetUtcNow())
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.SlotUnavailable);
        }

        var isException = await database.ScheduleExceptions.AnyAsync(
            item => item.OrganizationId == organizationId &&
                item.PractitionerUserId == practitionerUserId &&
                item.LocalDate == localDate,
            cancellationToken);
        if (isException)
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.SlotUnavailable);
        }

        var rules = await database.ScheduleRules
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.PractitionerUserId == practitionerUserId &&
                item.DayOfWeek == localDate.DayOfWeek)
            .ToListAsync(cancellationToken);
        var dailySchedule = SchedulePreviewCalculator.Calculate(
            localDate,
            days: 1,
            rules,
            new HashSet<DateOnly>(),
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes)[0];
        if (!dailySchedule.PotentialSlots.Any(slot => slot.StartTime == localStartTime))
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.SlotUnavailable);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var appointment = Appointment.Create(
            organizationId,
            practitionerUserId,
            idempotencyKey,
            patientName,
            contactEmail,
            contactPhone,
            contactConsent,
            startsAtUtc,
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes,
            AppointmentConfirmationPolicy.GetInitialStatus(
                organization.AppointmentConfirmationMode),
            nowUtc);

        var hasConflict = await database.Appointments.AnyAsync(
            item => item.OrganizationId == organizationId &&
                item.PractitionerUserId == practitionerUserId &&
                (item.Status == AppointmentStatus.Requested ||
                    item.Status == AppointmentStatus.Confirmed) &&
                item.StartsAtUtc < appointment.OccupiedUntilUtc &&
                item.OccupiedUntilUtc > appointment.StartsAtUtc,
            cancellationToken);
        if (hasConflict)
        {
            return AppointmentBookingResult.Failed(AppointmentBookingError.SlotUnavailable);
        }

        database.Appointments.Add(appointment);
        var auditEvent = AppointmentAuditEvent.Create(
            organizationId,
            appointment.Id,
            actorUserId ?? practitionerUserId,
            previousStatus: null,
            newStatus: appointment.Status,
            changedAtUtc: nowUtc);
        database.AppointmentAuditEvents.Add(auditEvent);
        var practitionerEmail = await database.Users
            .Where(item => item.Id == practitionerUserId)
            .Select(item => item.Email)
            .SingleOrDefaultAsync(cancellationToken);
        database.AppointmentNotificationOutboxMessages.AddRange(
            AppointmentNotificationOutboxMessage.CreateForEvent(
                appointment,
                auditEvent,
                practitionerEmail,
                nowUtc));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AppointmentBookingResult.Succeeded(appointment);
    }
}
