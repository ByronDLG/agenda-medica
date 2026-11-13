using AgendaMedica.Api.Domain.Appointments;

namespace AgendaMedica.Api.Infrastructure.Appointments;

public enum AppointmentBookingError
{
    None,
    NotPractitioner,
    OrganizationNotFound,
    PublicLinkNotFound,
    SecretaryNotAssigned,
    InvalidLocalTime,
    InvalidRequest,
    SlotUnavailable,
    IdempotencyKeyReused,
}

public sealed record AppointmentBookingResult(
    AppointmentBookingError Error,
    Appointment? Appointment)
{
    public static AppointmentBookingResult Failed(AppointmentBookingError error) =>
        new(error, null);

    public static AppointmentBookingResult Succeeded(Appointment appointment) =>
        new(AppointmentBookingError.None, appointment);
}
