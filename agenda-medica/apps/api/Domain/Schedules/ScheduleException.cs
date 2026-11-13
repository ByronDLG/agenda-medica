namespace AgendaMedica.Api.Domain.Schedules;

public sealed class ScheduleException
{
    public Guid OrganizationId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public static ScheduleException Create(
        Guid organizationId,
        Guid practitionerUserId,
        DateOnly localDate)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        }

        if (practitionerUserId == Guid.Empty)
        {
            throw new ArgumentException("Practitioner user ID is required.", nameof(practitionerUserId));
        }

        return new ScheduleException
        {
            OrganizationId = organizationId,
            PractitionerUserId = practitionerUserId,
            LocalDate = localDate,
        };
    }
}
