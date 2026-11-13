using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Domain.Schedules;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/schedule")]
public sealed class SchedulesController(
    AgendaDbContext database,
    IAntiforgery antiforgery,
    PractitionerAgendaAccessService agendaAccess,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaximumWindowsPerWeek = 56;
    private const string TimeFormat = "HH:mm";
    private const int MaximumPreviewDays = 31;
    private const int MaximumExceptionDaysAhead = 365;

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        var timeZoneId = await database.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        if (timeZoneId is null)
        {
            return NotFound();
        }

        var membership = await database.OrganizationMemberships
            .AsNoTracking()
            .SingleAsync(
                item => item.OrganizationId == organizationId && item.UserId == userId,
                cancellationToken);
        var rules = await database.ScheduleRules
            .AsNoTracking()
            .Where(rule =>
                rule.OrganizationId == organizationId &&
                rule.PractitionerUserId == userId)
            .OrderBy(rule => rule.DayOfWeek)
            .ThenBy(rule => rule.StartTime)
            .Select(rule => new WeeklyScheduleWindowResponse(
                (int)rule.DayOfWeek,
                rule.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                rule.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);

        return Ok(new WeeklyScheduleResponse(
            timeZoneId,
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes,
            rules));
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        Guid organizationId,
        UpdateWeeklyScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        if (request.AppointmentDurationMinutes is not (15 or 20 or 30 or 45 or 60) ||
            request.PreparationBufferMinutes is not (0 or 5 or 10 or 15) ||
            request.Windows.Length > MaximumWindowsPerWeek ||
            !TryCreateRules(organizationId, userId, request.Windows, out var rules))
        {
            return BadRequest(new
            {
                message = $"El horario debe tener una duración y preparación permitidas y hasta {MaximumWindowsPerWeek} intervalos válidos sin solapamientos.",
            });
        }

        var timeZoneId = await database.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        if (timeZoneId is null)
        {
            return NotFound();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            userId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return Forbid();
        }

        var existingRules = database.ScheduleRules.Where(rule =>
            rule.OrganizationId == organizationId &&
            rule.PractitionerUserId == userId);
        await existingRules.ExecuteDeleteAsync(cancellationToken);
        database.ScheduleRules.AddRange(rules);
        membership.UpdateSchedulePreferences(
            request.AppointmentDurationMinutes,
            request.PreparationBufferMinutes);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new WeeklyScheduleResponse(
            timeZoneId,
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes,
            rules
                .OrderBy(rule => rule.DayOfWeek)
                .ThenBy(rule => rule.StartTime)
                .Select(rule => new WeeklyScheduleWindowResponse(
                    (int)rule.DayOfWeek,
                    rule.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                    rule.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)))
                .ToArray()));
    }

    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview(
        Guid organizationId,
        [FromQuery] Guid? practitionerUserId,
        [FromQuery, Range(1, MaximumPreviewDays)] int days = 14,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        var role = await agendaAccess.GetRoleAsync(organizationId, userId, cancellationToken);
        if (role is not (OrganizationRole.Practitioner or OrganizationRole.Secretary))
        {
            return Forbid();
        }

        if (role == OrganizationRole.Secretary && practitionerUserId is null)
        {
            return BadRequest(new { message = "Selecciona una agenda autorizada." });
        }

        var targetPractitionerId = practitionerUserId ?? userId;
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            targetPractitionerId,
            cancellationToken);
        if (membership is null ||
            membership.Role != OrganizationRole.Practitioner ||
            !await agendaAccess.CanAccessPractitionerAgendaAsync(
                organizationId,
                userId,
                targetPractitionerId,
                cancellationToken))
        {
            return Forbid();
        }

        var scheduleClock = await GetScheduleClockAsync(organizationId, cancellationToken);
        if (scheduleClock is null)
        {
            return NotFound();
        }

        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), scheduleClock.Value.TimeZone)
                .DateTime);
        var endDate = localToday.AddDays(days);
        var rules = await database.ScheduleRules
            .AsNoTracking()
            .Where(rule =>
                rule.OrganizationId == organizationId &&
                rule.PractitionerUserId == targetPractitionerId)
            .ToListAsync(cancellationToken);
        var closedDates = await database.ScheduleExceptions
            .AsNoTracking()
            .Where(exception =>
                exception.OrganizationId == organizationId &&
                exception.PractitionerUserId == targetPractitionerId &&
                exception.LocalDate >= localToday &&
                exception.LocalDate < endDate)
            .Select(exception => exception.LocalDate)
            .ToListAsync(cancellationToken);
        var preview = SchedulePreviewCalculator.Calculate(
            localToday,
            days,
            rules,
            closedDates.ToHashSet(),
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes);

        await transaction.CommitAsync(cancellationToken);
        return Ok(new SchedulePreviewResponse(
            scheduleClock.Value.TimeZoneId,
            membership.AppointmentDurationMinutes,
            membership.PreparationBufferMinutes,
            preview.Select(day => new SchedulePreviewDayResponse(
                day.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                (int)day.LocalDate.DayOfWeek,
                day.IsException,
                day.Windows.Select(window => new SchedulePreviewWindowResponse(
                    window.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                    window.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)))
                    .ToArray(),
                day.PotentialSlots.Select(slot => new SchedulePreviewWindowResponse(
                    slot.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                    slot.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)))
                    .ToArray()))
                .ToArray()));
    }

    [HttpGet("exceptions")]
    public async Task<IActionResult> GetExceptions(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        var scheduleClock = await GetScheduleClockAsync(organizationId, cancellationToken);
        if (scheduleClock is null)
        {
            return NotFound();
        }

        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), scheduleClock.Value.TimeZone)
                .DateTime);
        var throughDate = localToday.AddDays(MaximumExceptionDaysAhead);
        var dates = await database.ScheduleExceptions
            .AsNoTracking()
            .Where(exception =>
                exception.OrganizationId == organizationId &&
                exception.PractitionerUserId == userId &&
                exception.LocalDate >= localToday &&
                exception.LocalDate <= throughDate)
            .OrderBy(exception => exception.LocalDate)
            .Select(exception => exception.LocalDate)
            .ToListAsync(cancellationToken);

        return Ok(new ScheduleExceptionsResponse(
            scheduleClock.Value.TimeZoneId,
            dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .ToArray()));
    }

    [HttpPost("exceptions")]
    public async Task<IActionResult> AddException(
        Guid organizationId,
        CreateScheduleExceptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        var scheduleClock = await GetScheduleClockAsync(organizationId, cancellationToken);
        if (scheduleClock is null)
        {
            return NotFound();
        }

        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), scheduleClock.Value.TimeZone)
                .DateTime);
        if (request.LocalDate < localToday ||
            request.LocalDate > localToday.AddDays(MaximumExceptionDaysAhead))
        {
            return BadRequest(new
            {
                message = $"La fecha debe estar entre hoy y los próximos {MaximumExceptionDaysAhead} días.",
            });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            userId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return Forbid();
        }

        var exists = await database.ScheduleExceptions.AnyAsync(
            exception =>
                exception.OrganizationId == organizationId &&
                exception.PractitionerUserId == userId &&
                exception.LocalDate == request.LocalDate,
            cancellationToken);
        if (exists)
        {
            return Conflict(new { message = "Ese día ya está marcado como no laborable." });
        }

        database.ScheduleExceptions.Add(
            ScheduleException.Create(organizationId, userId, request.LocalDate));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("exceptions/{localDate}")]
    public async Task<IActionResult> RemoveException(
        Guid organizationId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId) ||
            !await IsPractitionerAsync(organizationId, userId, cancellationToken))
        {
            return Forbid();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            userId,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return Forbid();
        }

        await database.ScheduleExceptions
            .Where(exception =>
                exception.OrganizationId == organizationId &&
                exception.PractitionerUserId == userId &&
                exception.LocalDate == localDate)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<(string TimeZoneId, TimeZoneInfo TimeZone)?> GetScheduleClockAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await database.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        if (timeZoneId is null)
        {
            return null;
        }

        return (timeZoneId, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }

    private Task<bool> IsPractitionerAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        database.OrganizationMemberships.AnyAsync(
            membership => membership.OrganizationId == organizationId &&
                membership.UserId == userId &&
                membership.Role == OrganizationRole.Practitioner,
            cancellationToken);

    private static bool TryCreateRules(
        Guid organizationId,
        Guid userId,
        IReadOnlyList<WeeklyScheduleWindowRequest> windows,
        out List<ScheduleRule> rules)
    {
        rules = [];
        var parsed = new List<(DayOfWeek Day, TimeOnly Start, TimeOnly End)>(windows.Count);
        foreach (var window in windows)
        {
            if (window.DayOfWeek is < 0 or > 6 ||
                !TimeOnly.TryParseExact(
                    window.StartTime,
                    TimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var start) ||
                !TimeOnly.TryParseExact(
                    window.EndTime,
                    TimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var end) ||
                start >= end)
            {
                return false;
            }

            parsed.Add(((DayOfWeek)window.DayOfWeek, start, end));
        }

        rules = parsed
            .Select(window => ScheduleRule.Create(
                organizationId,
                userId,
                window.Day,
                window.Start,
                window.End))
            .ToList();
        if (ScheduleRule.HasOverlappingWindows(rules))
        {
            rules = [];
            return false;
        }

        return true;
    }
}

public sealed record UpdateWeeklyScheduleRequest(
    [param: Required]
    [param: MaxLength(56)]
    WeeklyScheduleWindowRequest[] Windows,
    [param: Range(15, 60)]
    int AppointmentDurationMinutes,
    [param: Range(0, 15)]
    int PreparationBufferMinutes);

public sealed record WeeklyScheduleWindowRequest(
    [param: Range(0, 6)]
    int DayOfWeek,
    [param: Required]
    [param: RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    string StartTime,
    [param: Required]
    [param: RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    string EndTime);

public sealed record WeeklyScheduleWindowResponse(
    int DayOfWeek,
    string StartTime,
    string EndTime);

public sealed record WeeklyScheduleResponse(
    string TimeZoneId,
    int AppointmentDurationMinutes,
    int PreparationBufferMinutes,
    IReadOnlyList<WeeklyScheduleWindowResponse> Windows);

public sealed record CreateScheduleExceptionRequest(
    [param: Required]
    DateOnly LocalDate);

public sealed record ScheduleExceptionsResponse(
    string TimeZoneId,
    IReadOnlyList<string> Dates);

public sealed record SchedulePreviewResponse(
    string TimeZoneId,
    int AppointmentDurationMinutes,
    int PreparationBufferMinutes,
    IReadOnlyList<SchedulePreviewDayResponse> Days);

public sealed record SchedulePreviewDayResponse(
    string LocalDate,
    int DayOfWeek,
    bool IsException,
    IReadOnlyList<SchedulePreviewWindowResponse> Windows,
    IReadOnlyList<SchedulePreviewWindowResponse> PotentialSlots);

public sealed record SchedulePreviewWindowResponse(string StartTime, string EndTime);
