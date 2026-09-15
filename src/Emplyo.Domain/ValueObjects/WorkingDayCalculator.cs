namespace Emplyo.Domain.ValueObjects;

/// <summary>
/// Pure domain logic for counting working days between two dates, excluding
/// weekends and a supplied set of holiday dates.
/// </summary>
public static class WorkingDayCalculator
{
    /// <summary>
    /// Calculates inclusive working days between <paramref name="start"/> and
    /// <paramref name="end"/>. Saturdays and Sundays are excluded. Any date
    /// present in <paramref name="holidays"/> is excluded.
    /// </summary>
    public static decimal CountWorkingDays(DateOnly start, DateOnly end, IEnumerable<DateOnly>? holidays = null)
    {
        if (end < start)
        {
            return 0m;
        }

        var holidaySet = holidays is null
            ? new HashSet<DateOnly>()
            : new HashSet<DateOnly>(holidays);

        decimal days = 0m;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (holidaySet.Contains(date))
            {
                continue;
            }

            days += 1m;
        }

        return days;
    }

    /// <summary>
    /// Determines whether the given date range overlaps an existing range.
    /// Ranges are inclusive on both ends.
    /// </summary>
    public static bool RangesOverlap(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd)
        => aStart <= bEnd && bStart <= aEnd;
}
