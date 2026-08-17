namespace Avtomagazin.Contracts;

/// <summary>Europe/Minsk (UTC+3, no DST). Shared by service-day bounds and on-time windows.</summary>
public static class BelarusTime
{
    public static TimeZoneInfo Zone { get; } = Resolve();

    public static DateTimeOffset ToUtc(DateTime localUnspecified)
        => new(TimeZoneInfo.ConvertTimeToUtc(localUnspecified, Zone), TimeSpan.Zero);

    public static DateTimeOffset TodayAtLocalClock(DateTimeOffset utcNow, int hour, int minute)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, Zone);
        var at = new DateTime(local.Year, local.Month, local.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return ToUtc(at);
    }

    public static DateTimeOffset ToMinsk(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTime(utc, Zone);

    public static string Clock(DateTimeOffset utc)
        => ToMinsk(utc).ToString("HH:mm");

    public static string DateClock(DateTimeOffset utc)
        => ToMinsk(utc).ToString("dd.MM HH:mm");

    public static string InputLocal(DateTimeOffset utc)
        => ToMinsk(utc).ToString("yyyy-MM-dd'T'HH:mm");

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Minsk", "Belarus Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Europe/Minsk", TimeSpan.FromHours(3), "Minsk", "Minsk");
    }
}
