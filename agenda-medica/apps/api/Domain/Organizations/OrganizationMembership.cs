using AgendaMedica.Api.Domain.Identity;

namespace AgendaMedica.Api.Domain.Organizations;

public sealed class OrganizationMembership
{
    public Guid UserId { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    public Guid OrganizationId { get; private set; }

    public Organization Organization { get; private set; } = null!;

    public OrganizationRole Role { get; private set; }

    public int AppointmentDurationMinutes { get; private set; } = 30;

    public int PreparationBufferMinutes { get; private set; } = 5;

    public static OrganizationMembership Create(
        ApplicationUser user,
        Organization organization,
        OrganizationRole role)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(organization);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        return new OrganizationMembership
        {
            User = user,
            UserId = user.Id,
            Organization = organization,
            OrganizationId = organization.Id,
            Role = role,
        };
    }

    public void UpdateSchedulePreferences(
        int appointmentDurationMinutes,
        int preparationBufferMinutes)
    {
        if (appointmentDurationMinutes is not (15 or 20 or 30 or 45 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(appointmentDurationMinutes));
        }

        if (preparationBufferMinutes is not (0 or 5 or 10 or 15))
        {
            throw new ArgumentOutOfRangeException(nameof(preparationBufferMinutes));
        }

        AppointmentDurationMinutes = appointmentDurationMinutes;
        PreparationBufferMinutes = preparationBufferMinutes;
    }
}
