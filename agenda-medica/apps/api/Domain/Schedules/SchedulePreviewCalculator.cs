namespace AgendaMedica.Api.Domain.Schedules;

public sealed record SchedulePreviewWindow(TimeOnly StartTime, TimeOnly EndTime);

public sealed record SchedulePreviewSlot(TimeOnly StartTime, TimeOnly EndTime);

public sealed record SchedulePreviewDay(
    DateOnly LocalDate,
    bool IsException,
    IReadOnlyList<SchedulePreviewWindow> Windows,
    IReadOnlyList<SchedulePreviewSlot> PotentialSlots);

public static class SchedulePreviewCalculator
{
    public static IReadOnlyList<SchedulePreviewDay> Calculate(
        DateOnly from,
        int days,
        IReadOnlyCollection<ScheduleRule> rules,
        IReadOnlySet<DateOnly> closedDates,
        int appointmentDurationMinutes,
        int preparationBufferMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, 31);
        if (appointmentDurationMinutes is not (15 or 20 or 30 or 45 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(appointmentDurationMinutes));
        }

        if (preparationBufferMinutes is not (0 or 5 or 10 or 15))
        {
            throw new ArgumentOutOfRangeException(nameof(preparationBufferMinutes));
        }

        var rulesByDay = rules
            .GroupBy(rule => rule.DayOfWeek)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var preview = new List<SchedulePreviewDay>(days);

        for (var offset = 0; offset < days; offset++)
        {
            var date = from.AddDays(offset);
            var isException = closedDates.Contains(date);
            var windows = isException || !rulesByDay.TryGetValue(date.DayOfWeek, out var dayRules)
                ? Array.Empty<SchedulePreviewWindow>()
                : dayRules
                    .OrderBy(rule => rule.StartTime)
                    .Select(rule => new SchedulePreviewWindow(rule.StartTime, rule.EndTime))
                    .ToArray();
            var potentialSlots = windows
                .SelectMany(window => CreateSlots(
                    window,
                    appointmentDurationMinutes,
                    preparationBufferMinutes))
                .ToArray();

            preview.Add(new SchedulePreviewDay(date, isException, windows, potentialSlots));
        }

        return preview;
    }

    private static IEnumerable<SchedulePreviewSlot> CreateSlots(
        SchedulePreviewWindow window,
        int appointmentDurationMinutes,
        int preparationBufferMinutes)
    {
        var appointmentStart = (int)window.StartTime.ToTimeSpan().TotalMinutes;
        var windowEnd = (int)window.EndTime.ToTimeSpan().TotalMinutes;
        var occupiedMinutes = appointmentDurationMinutes + preparationBufferMinutes;
        for (var startMinute = appointmentStart;
             startMinute + occupiedMinutes <= windowEnd;
             startMinute += occupiedMinutes)
        {
            yield return new SchedulePreviewSlot(
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(startMinute)),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(startMinute + appointmentDurationMinutes)));
        }
    }
}
