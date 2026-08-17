namespace Avtomagazin.Contracts;

/// <summary>
/// On-time window for a stop: 15 minutes before planned clock time through 1 hour after.
/// Planned dates are treated as a daily timetable in Europe/Minsk (hour:minute applied to today).
/// </summary>
public static class ScheduleWindow
{
    public const int OpenMinutesBefore = 15;
    public const int CloseHoursAfter = 1;

    public static bool Contains(DateTimeOffset plannedUtc, DateTimeOffset now)
    {
        var (opens, closes) = Bounds(plannedUtc, now);
        return now >= opens && now <= closes;
    }

    public static (DateTimeOffset OpensAtUtc, DateTimeOffset ClosesAtUtc) Bounds(
        DateTimeOffset plannedUtc,
        DateTimeOffset now)
    {
        var plannedLocal = TimeZoneInfo.ConvertTime(plannedUtc, BelarusTime.Zone);
        var today = BelarusTime.TodayAtLocalClock(now, plannedLocal.Hour, plannedLocal.Minute);
        var anchor = Inside(plannedUtc, now) ? plannedUtc : today;
        return (anchor.AddMinutes(-OpenMinutesBefore), anchor.AddHours(CloseHoursAfter));
    }

    private static bool Inside(DateTimeOffset plannedUtc, DateTimeOffset now)
        => now >= plannedUtc.AddMinutes(-OpenMinutesBefore) && now <= plannedUtc.AddHours(CloseHoursAfter);
}
