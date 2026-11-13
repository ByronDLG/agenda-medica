namespace AgendaMedica.Api.Domain.Schedules;

public sealed class ScheduleRule
{
    public Guid OrganizationId { get; private set; }

    public Guid PractitionerUserId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public static ScheduleRule Create(
        Guid organizationId,
        Guid practitionerUserId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        }

        if (practitionerUserId == Guid.Empty)
        {
            throw new ArgumentException("Practitioner user ID is required.", nameof(practitionerUserId));
        }

        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        }

        if (startTime >= endTime)
        {
            throw new ArgumentException("Schedule start time must be earlier than end time.");
        }

        return new ScheduleRule
        {
            OrganizationId = organizationId,
            PractitionerUserId = practitionerUserId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
        };
    }

    public static bool HasOverlappingWindows(IEnumerable<ScheduleRule> rules) =>
        rules
            .GroupBy(rule => rule.DayOfWeek)
            .Any(day =>
            {
                var ordered = day.OrderBy(rule => rule.StartTime).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                {
                    if (ordered[index].StartTime < ordered[index - 1].EndTime)
                    {
                        return true;
                    }
                }

                return false;
            });
}
