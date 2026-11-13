using System.Globalization;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public sealed class PublicBookingAvailabilityService(
    AgendaDbContext database,
    TimeProvider timeProvider)
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    public async Task<PublicBookingAvailability?> GetAsync(
        byte[] tokenHash,
        int days,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tokenHash.Length, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, 31);

        var context = await GetBookingContextAsync(tokenHash, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
        var now = timeProvider.GetUtcNow();
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var throughDate = localToday.AddDays(days);
        var rules = await database.ScheduleRules
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == context.OrganizationId &&
                item.PractitionerUserId == context.PractitionerUserId)
            .ToListAsync(cancellationToken);
        var closedDates = await database.ScheduleExceptions
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == context.OrganizationId &&
                item.PractitionerUserId == context.PractitionerUserId &&
                item.LocalDate >= localToday &&
                item.LocalDate < throughDate)
            .Select(item => item.LocalDate)
            .ToListAsync(cancellationToken);
        var appointments = await database.Appointments
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == context.OrganizationId &&
                item.PractitionerUserId == context.PractitionerUserId &&
                (item.Status == AppointmentStatus.Requested ||
                    item.Status == AppointmentStatus.Confirmed) &&
                item.StartsAtUtc < now.AddDays(days + 1) &&
                item.OccupiedUntilUtc > now)
            .Select(item => new OccupiedAppointment(item.StartsAtUtc, item.OccupiedUntilUtc))
            .ToListAsync(cancellationToken);
        var preview = SchedulePreviewCalculator.Calculate(
            localToday,
            days,
            rules,
            closedDates.ToHashSet(),
            context.AppointmentDurationMinutes,
            context.PreparationBufferMinutes);

        var availableDays = preview.Select(day =>
        {
            var slots = day.PotentialSlots
                .Where(slot => IsAvailable(
                    day.LocalDate,
                    slot,
                    timeZone,
                    now,
                    context.AppointmentDurationMinutes,
                    context.PreparationBufferMinutes,
                    appointments))
                .Select(slot => new PublicBookingSlot(
                    slot.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                    slot.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)))
                .ToArray();
            return new PublicBookingDay(
                day.LocalDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                slots);
        }).ToArray();

        return new PublicBookingAvailability(
            context.OrganizationName,
            context.TimeZoneId,
            context.AppointmentDurationMinutes,
            availableDays);
    }

    public async Task<PublicBookingContext?> GetBookingContextAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tokenHash.Length, 32);

        var link = await database.PublicBookingLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var membership = await database.OrganizationMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OrganizationId == link.OrganizationId &&
                    item.UserId == link.PractitionerUserId &&
                    item.Role == OrganizationRole.Practitioner,
                cancellationToken);
        var organization = await database.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == link.OrganizationId, cancellationToken);
        if (membership is null || organization is null)
        {
            return null;
        }

        return new PublicBookingContext(
            link.OrganizationId,
            link.PractitionerUserId,
            organization.Name,
            organization.TimeZoneId,
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes);
    }

    private static bool IsAvailable(
        DateOnly localDate,
        SchedulePreviewSlot slot,
        TimeZoneInfo timeZone,
        DateTimeOffset now,
        int appointmentDurationMinutes,
        int preparationBufferMinutes,
        IReadOnlyCollection<OccupiedAppointment> appointments)
    {
        var localStart = localDate.ToDateTime(slot.StartTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localStart) || timeZone.IsAmbiguousTime(localStart))
        {
            return false;
        }

        var startsAtUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
        if (startsAtUtc <= now)
        {
            return false;
        }

        var occupiedUntilUtc = startsAtUtc
            .AddMinutes(appointmentDurationMinutes + preparationBufferMinutes);
        return !appointments.Any(appointment =>
            appointment.StartsAtUtc < occupiedUntilUtc &&
            appointment.OccupiedUntilUtc > startsAtUtc);
    }

    private sealed record OccupiedAppointment(
        DateTimeOffset StartsAtUtc,
        DateTimeOffset OccupiedUntilUtc);
}

public sealed record PublicBookingAvailability(
    string OrganizationName,
    string TimeZoneId,
    int AppointmentDurationMinutes,
    IReadOnlyList<PublicBookingDay> Days);

public sealed record PublicBookingDay(
    string LocalDate,
    IReadOnlyList<PublicBookingSlot> Slots);

public sealed record PublicBookingSlot(string StartTime, string EndTime);

public sealed record PublicBookingContext(
    Guid OrganizationId,
    Guid PractitionerUserId,
    string OrganizationName,
    string TimeZoneId,
    int AppointmentDurationMinutes,
    int PreparationBufferMinutes);
