using AgendaMedica.Api.Domain.Organizations;

namespace AgendaMedica.Api.Domain;

public sealed class Organization
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string CountryCode { get; private set; } = "GT";

    public string TimeZoneId { get; private set; } = "America/Guatemala";

    public AppointmentConfirmationMode AppointmentConfirmationMode { get; private set; } =
        AppointmentConfirmationMode.ManualApproval;

    public ICollection<OrganizationMembership> Memberships { get; private set; } = [];

    public static Organization Create(
        string name,
        AppointmentConfirmationMode confirmationMode =
            AppointmentConfirmationMode.ManualApproval,
        string countryCode = "GT",
        string timeZoneId = "America/Guatemala")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (!Enum.IsDefined(confirmationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationMode));
        }

        return new Organization
        {
            Name = name.Trim(),
            CountryCode = countryCode,
            TimeZoneId = timeZoneId,
            AppointmentConfirmationMode = confirmationMode,
        };
    }

    public void UpdateAppointmentConfirmationMode(AppointmentConfirmationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        AppointmentConfirmationMode = mode;
    }
}
