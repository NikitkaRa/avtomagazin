namespace Avtomagazin.Contracts;

/// <summary>
/// On-time window for a stop: 15 minutes before planned clock time through 1 hour after.
/// Planned dates are treated as a daily timetable (hour:minute applied to today).
/// </summary>
public static class ScheduleWindow
{
    public static bool Contains(DateTimeOffset plannedUtc, DateTimeOffset now)
    {
        if (Inside(plannedUtc, now))
        {
            return true;
        }

        var today = new DateTimeOffset(now.Year, now.Month, now.Day, plannedUtc.Hour, plannedUtc.Minute, 0, TimeSpan.Zero);
        return Inside(today, now);
    }

    private static bool Inside(DateTimeOffset plannedUtc, DateTimeOffset now)
        => now >= plannedUtc.AddMinutes(-15) && now <= plannedUtc.AddHours(1);
}
