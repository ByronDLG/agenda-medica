using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Domain.Organizations;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/appointments")]
public sealed class AppointmentsController(
    AgendaDbContext database,
    AppointmentBookingService bookingService,
    PractitionerAgendaAccessService agendaAccess,
    IAntiforgery antiforgery,
    TimeProvider timeProvider) : ControllerBase
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid organizationId,
        [FromQuery] Guid? practitionerUserId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
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
        if (!await agendaAccess.CanAccessPractitionerAgendaAsync(
                organizationId,
                userId,
                targetPractitionerId,
                cancellationToken))
        {
            return Forbid();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var targetMembership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            targetPractitionerId,
            cancellationToken);
        if (targetMembership?.Role != OrganizationRole.Practitioner ||
            !await agendaAccess.CanAccessPractitionerAgendaAsync(
                organizationId,
                userId,
                targetPractitionerId,
                cancellationToken))
        {
            return Forbid();
        }

        var timeZoneId = await database.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => item.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        if (timeZoneId is null)
        {
            return NotFound();
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = timeProvider.GetUtcNow();
        var appointments = await database.Appointments
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.PractitionerUserId == targetPractitionerId &&
                (item.Status == AppointmentStatus.Requested ||
                    item.Status == AppointmentStatus.Confirmed) &&
                item.StartsAtUtc >= now.AddDays(-31) &&
                item.StartsAtUtc < now.AddDays(31))
            .OrderBy(item => item.StartsAtUtc)
            .Select(item => new
            {
                item.Id,
                item.PatientName,
                item.ContactEmail,
                item.ContactPhone,
                item.StartsAtUtc,
                item.EndsAtUtc,
                item.OccupiedUntilUtc,
                item.DurationMinutes,
                item.Status,
            })
            .ToListAsync(cancellationToken);

        var response = new AppointmentListResponse(
            timeZoneId,
            appointments.Select(item => new AppointmentResponse(
                item.Id,
                item.PatientName,
                item.ContactEmail,
                item.ContactPhone,
                TimeZoneInfo.ConvertTime(item.StartsAtUtc, timeZone)
                    .ToString(DateFormat, CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(item.StartsAtUtc, timeZone)
                    .ToString(TimeFormat, CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(item.EndsAtUtc, timeZone)
                    .ToString(TimeFormat, CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(item.OccupiedUntilUtc, timeZone)
                    .ToString(DateFormat, CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(item.OccupiedUntilUtc, timeZone)
                    .ToString(TimeFormat, CultureInfo.InvariantCulture),
                item.DurationMinutes,
                item.Status.ToString()))
                .ToArray());
        await transaction.CommitAsync(cancellationToken);
        return Ok(response);
    }

    [HttpGet("practitioners")]
    public async Task<IActionResult> GetAuthorizedPractitioners(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        var role = await agendaAccess.GetRoleAsync(organizationId, userId, cancellationToken);
        if (role is not (OrganizationRole.Practitioner or OrganizationRole.Secretary))
        {
            return Forbid();
        }

        var agendas = await agendaAccess.GetAuthorizedAgendasAsync(
            organizationId,
            userId,
            cancellationToken);
        return Ok(agendas.Select(item => new AuthorizedPractitionerResponse(
            item.UserId,
            item.Email)).ToArray());
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid organizationId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyValue,
        CreateAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        var role = await agendaAccess.GetRoleAsync(organizationId, userId, cancellationToken);
        if (role is not (OrganizationRole.Practitioner or OrganizationRole.Secretary))
        {
            return Forbid();
        }

        var targetPractitionerId = request.PractitionerUserId ?? userId;
        if (role == OrganizationRole.Secretary && request.PractitionerUserId is null)
        {
            return BadRequest(new { message = "Selecciona una agenda autorizada." });
        }

        if (!await agendaAccess.CanAccessPractitionerAgendaAsync(
                organizationId,
                userId,
                targetPractitionerId,
                cancellationToken))
        {
            return Forbid();
        }

        if (!Guid.TryParse(idempotencyKeyValue, out var idempotencyKey) ||
            idempotencyKey == Guid.Empty)
        {
            return BadRequest(new { message = "La clave de solicitud no es válida." });
        }

        var result = await bookingService.BookAsync(
            organizationId,
            targetPractitionerId,
            idempotencyKey,
            request.LocalDate,
            request.StartTime,
            request.PatientName,
            request.ContactEmail,
            request.ContactPhone,
            request.ContactConsent,
            cancellationToken,
            actorUserId: userId,
            authorizedSecretaryUserId: role == OrganizationRole.Secretary ? userId : null);
        if (result.Error != AppointmentBookingError.None || result.Appointment is null)
        {
            return result.Error switch
            {
                AppointmentBookingError.NotPractitioner or
                    AppointmentBookingError.SecretaryNotAssigned => Forbid(),
                AppointmentBookingError.OrganizationNotFound => NotFound(),
                AppointmentBookingError.InvalidLocalTime or
                    AppointmentBookingError.InvalidRequest => BadRequest(new
                    {
                        message = "Revisa la fecha, hora y datos de contacto. Se requiere consentimiento.",
                    }),
                AppointmentBookingError.SlotUnavailable => Conflict(new
                {
                    message = "Ese horario ya no está disponible.",
                }),
                AppointmentBookingError.IdempotencyKeyReused => Conflict(new
                {
                    message = "La clave de solicitud ya se utilizó con otros datos.",
                }),
                _ => throw new InvalidOperationException(
                    $"Unexpected appointment booking result: {result.Error}."),
            };
        }

        return CreatedAtAction(
            nameof(Get),
            new { organizationId, practitionerUserId = targetPractitionerId },
            ToResponse(result.Appointment, await GetTimeZoneAsync(organizationId, cancellationToken)));
    }

    [HttpPatch("{appointmentId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid organizationId,
        Guid appointmentId,
        UpdateAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        if (!Enum.TryParse(request.Status, ignoreCase: false, out AppointmentStatus status) ||
            !Enum.IsDefined(status) ||
            status is not (AppointmentStatus.Confirmed or
                AppointmentStatus.Rejected or
                AppointmentStatus.Cancelled or
                AppointmentStatus.Completed or
                AppointmentStatus.NoShow))
        {
            return BadRequest(new { message = "El estado solicitado no es válido." });
        }

        var role = await agendaAccess.GetRoleAsync(organizationId, userId, cancellationToken);
        if (role is not (OrganizationRole.Practitioner or OrganizationRole.Secretary))
        {
            return Forbid();
        }

        if (role == OrganizationRole.Secretary &&
            status is not (AppointmentStatus.Confirmed or
                AppointmentStatus.Rejected or
                AppointmentStatus.Cancelled))
        {
            return Forbid();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var targetPractitionerId = await database.Appointments
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organizationId &&
                item.Id == appointmentId)
            .Select(item => (Guid?)item.PractitionerUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (targetPractitionerId is null)
        {
            return NotFound();
        }

        var membership = await PractitionerScheduleLock.AcquireAsync(
            database,
            organizationId,
            targetPractitionerId.Value,
            cancellationToken);
        if (membership is null || membership.Role != OrganizationRole.Practitioner)
        {
            return NotFound();
        }

        if (!await agendaAccess.CanAccessPractitionerAgendaAsync(
                organizationId,
                userId,
                targetPractitionerId.Value,
                cancellationToken))
        {
            return NotFound();
        }

        var appointment = await database.Appointments.SingleOrDefaultAsync(
            item => item.Id == appointmentId &&
                item.OrganizationId == organizationId &&
                item.PractitionerUserId == targetPractitionerId.Value,
            cancellationToken);
        if (appointment is null)
        {
            return NotFound();
        }

        if (!appointment.CanTransitionTo(status))
        {
            return Conflict(new { message = "La cita ya no permite ese cambio de estado." });
        }

        var isAppointmentInPast = appointment.StartsAtUtc <= timeProvider.GetUtcNow();
        if ((!isAppointmentInPast &&
                status is AppointmentStatus.Completed or AppointmentStatus.NoShow) ||
            (isAppointmentInPast && status == AppointmentStatus.Confirmed) ||
            (isAppointmentInPast &&
                appointment.Status == AppointmentStatus.Confirmed &&
                status == AppointmentStatus.Cancelled))
        {
            return Conflict(new
            {
                message = "El cambio de estado no corresponde a la fecha de la cita.",
            });
        }

        var previousStatus = appointment.Status;
        appointment.ChangeStatus(status);
        database.AppointmentAuditEvents.Add(AppointmentAuditEvent.Create(
            organizationId,
            appointment.Id,
            userId,
            previousStatus,
            status,
            timeProvider.GetUtcNow()));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "La cita cambió en otra solicitud. Actualiza la agenda." });
        }

        return Ok(new { id = appointment.Id, status = appointment.Status.ToString() });
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<TimeZoneInfo> GetTimeZoneAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await database.Organizations
            .Where(item => item.Id == organizationId)
            .Select(item => item.TimeZoneId)
            .SingleAsync(cancellationToken);
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    private static AppointmentResponse ToResponse(
        Appointment appointment,
        TimeZoneInfo timeZone)
    {
        var localStart = TimeZoneInfo.ConvertTime(appointment.StartsAtUtc, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(appointment.EndsAtUtc, timeZone);
        return new AppointmentResponse(
            appointment.Id,
            appointment.PatientName,
            appointment.ContactEmail,
            appointment.ContactPhone,
            localStart.ToString(DateFormat, CultureInfo.InvariantCulture),
            localStart.ToString(TimeFormat, CultureInfo.InvariantCulture),
            localEnd.ToString(TimeFormat, CultureInfo.InvariantCulture),
            TimeZoneInfo.ConvertTime(appointment.OccupiedUntilUtc, timeZone)
                .ToString(DateFormat, CultureInfo.InvariantCulture),
            TimeZoneInfo.ConvertTime(appointment.OccupiedUntilUtc, timeZone)
                .ToString(TimeFormat, CultureInfo.InvariantCulture),
            appointment.DurationMinutes,
            appointment.Status.ToString());
    }
}

public sealed record CreateAppointmentRequest(
    [param: Required]
    DateOnly LocalDate,
    [param: Required]
    [param: RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    string StartTime,
    [param: Required]
    [param: StringLength(120, MinimumLength = 1)]
    string PatientName,
    [param: EmailAddress]
    [param: StringLength(254)]
    string? ContactEmail,
    [param: StringLength(32)]
    string? ContactPhone,
    bool ContactConsent,
    Guid? PractitionerUserId = null);

public sealed record UpdateAppointmentStatusRequest(
    [param: Required]
    [param: RegularExpression("^(Confirmed|Rejected|Cancelled|Completed|NoShow)$")]
    string Status);

public sealed record AppointmentListResponse(
    string TimeZoneId,
    IReadOnlyList<AppointmentResponse> Appointments);

public sealed record AuthorizedPractitionerResponse(Guid UserId, string? Email);

public sealed record AppointmentResponse(
    Guid Id,
    string PatientName,
    string? ContactEmail,
    string? ContactPhone,
    string LocalDate,
    string StartTime,
    string EndTime,
    string OccupiedUntilLocalDate,
    string OccupiedUntilTime,
    int DurationMinutes,
    string Status);
