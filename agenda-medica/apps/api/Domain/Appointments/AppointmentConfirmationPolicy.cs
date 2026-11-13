using AgendaMedica.Api.Domain.Organizations;

namespace AgendaMedica.Api.Domain.Appointments;

public static class AppointmentConfirmationPolicy
{
    public static AppointmentStatus GetInitialStatus(
        AppointmentConfirmationMode confirmationMode) =>
        confirmationMode switch
        {
            AppointmentConfirmationMode.ManualApproval => AppointmentStatus.Requested,
            AppointmentConfirmationMode.Automatic => AppointmentStatus.Confirmed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(confirmationMode),
                confirmationMode,
                "Unsupported appointment confirmation mode."),
        };
}
