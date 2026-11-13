using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using AgendaMedica.Api.Domain.Appointments;
using AgendaMedica.Api.Infrastructure.Appointments;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgendaMedica.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/public/booking")]
public sealed class PublicBookingController(
    AppointmentBookingService bookingService,
    PublicBookingAvailabilityService availabilityService,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("availability")]
    [EnableRateLimiting("public-booking-read")]
    public async Task<IActionResult> GetAvailability(
        [FromQuery, Range(1, 31)] int days = 14,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetTokenHash(out var tokenHash))
        {
            return NotFound();
        }

        var availability = await availabilityService.GetAsync(
            tokenHash,
            days,
            cancellationToken);
        return availability is null ? NotFound() : Ok(availability);
    }

    [HttpPost("requests")]
    [EnableRateLimiting("public-booking-write")]
    public async Task<IActionResult> CreateRequest(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyValue,
        CreatePublicAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return BadRequest();
        }

        if (!TryGetTokenHash(out var tokenHash))
        {
            return NotFound();
        }

        if (!Guid.TryParse(idempotencyKeyValue, out var idempotencyKey) ||
            idempotencyKey == Guid.Empty)
        {
            return BadRequest(new { message = "La clave de solicitud no es válida." });
        }

        var bookingContext = await availabilityService.GetBookingContextAsync(
            tokenHash,
            cancellationToken);
        if (bookingContext is null)
        {
            return NotFound();
        }

        var result = await bookingService.BookAsync(
            bookingContext.OrganizationId,
            bookingContext.PractitionerUserId,
            idempotencyKey,
            request.LocalDate,
            request.StartTime,
            request.PatientName,
            request.ContactEmail,
            request.ContactPhone,
            request.ContactConsent,
            cancellationToken,
            requiredPublicBookingTokenHash: tokenHash);
        if (result.Error != AppointmentBookingError.None || result.Appointment is null)
        {
            return result.Error switch
            {
                AppointmentBookingError.NotPractitioner or
                    AppointmentBookingError.PublicLinkNotFound or
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
                    $"Unexpected public appointment booking result: {result.Error}."),
            };
        }

        return StatusCode(
            StatusCodes.Status201Created,
            new PublicAppointmentRequestResponse(
                request.LocalDate.ToString("yyyy-MM-dd"),
                request.StartTime,
                result.Appointment.Status.ToString()));
    }

    private bool TryGetTokenHash(out byte[] tokenHash)
    {
        tokenHash = [];
        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return PublicBookingToken.TryHash(authorization.Parameter, out tokenHash);
    }
}

public sealed record CreatePublicAppointmentRequest(
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
    bool ContactConsent);

public sealed record PublicAppointmentRequestResponse(
    string LocalDate,
    string StartTime,
    string Status);
