using Avtomagazin.Contracts;

namespace Avtomagazin.UnitTests;

public class ScheduleWindowTests
{
    [Fact]
    public void Contains_planned_instant()
    {
        var planned = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        Assert.True(ScheduleWindow.Contains(planned, planned));
        Assert.True(ScheduleWindow.Contains(planned, planned.AddMinutes(-15)));
        Assert.True(ScheduleWindow.Contains(planned, planned.AddHours(1)));
        Assert.False(ScheduleWindow.Contains(planned, planned.AddMinutes(-16)));
        Assert.False(ScheduleWindow.Contains(planned, planned.AddHours(1).AddSeconds(1)));
    }

    [Fact]
    public void Contains_same_clock_time_on_another_day()
    {
        var planned = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);
        var today = new DateTimeOffset(2026, 8, 17, 9, 40, 0, TimeSpan.Zero);
        Assert.True(ScheduleWindow.Contains(planned, today));
        var (opens, closes) = ScheduleWindow.Bounds(planned, today);
        Assert.True(today >= opens && today <= closes);
        Assert.Equal(opens, BelarusTime.TodayAtLocalClock(today, 12, 30).AddMinutes(-15));
    }

    [Fact]
    public void Contains_uses_minsk_clock_when_now_has_local_offset()
    {
        var planned = new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 8, 17, 1, 10, 0, TimeSpan.FromHours(3));
        Assert.True(ScheduleWindow.Contains(planned, now));
    }
}
