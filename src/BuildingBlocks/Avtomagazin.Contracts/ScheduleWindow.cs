namespace Avtomagazin.Contracts;

/// <summary>
/// On-time window for a stop: 15 minutes before planned clock time through 1 hour after.
/// Planned dates are treated as a daily timetable in Europe/Minsk (hour:minute applied to today).
/// </summary>
public static class ScheduleWindow
{
    public static bool Contains(DateTimeOffset plannedUtc, DateTimeOffset now)
    {
        if (Inside(plannedUtc, now))
        {
            return true;
        }

        var plannedLocal = TimeZoneInfo.ConvertTime(plannedUtc, BelarusTime.Zone);
        var today = BelarusTime.TodayAtLocalClock(now, plannedLocal.Hour, plannedLocal.Minute);
        return Inside(today, now);
    }

    private static bool Inside(DateTimeOffset plannedUtc, DateTimeOffset now)
        => now >= plannedUtc.AddMinutes(-15) && now <= plannedUtc.AddHours(1);
}
