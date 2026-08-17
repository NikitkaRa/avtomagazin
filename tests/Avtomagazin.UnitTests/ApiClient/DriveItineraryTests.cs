using Avtomagazin.ApiClient;

namespace Avtomagazin.UnitTests.ApiClient;

public class DriveItineraryTests
{
    [Fact]
    public void FromStops_empty_says_no_stops_today()
    {
        var trip = DriveItinerary.FromStops([]);

        Assert.Null(trip.Next);
        Assert.Equal("Нет остановок на сегодня", trip.NextTitle);
        Assert.Empty(trip.NextMeta);
        Assert.Empty(trip.Rows);
    }

    [Fact]
    public void FromStops_picks_first_unvisited_and_marks_rows()
    {
        var first = Stop(1, "А", arrived: true);
        var second = Stop(2, "Б");
        var third = Stop(3, "В");

        var trip = DriveItinerary.FromStops([third, first, second]);

        Assert.Equal(second.Id, trip.Next?.Id);
        Assert.Equal("Б", trip.NextTitle);
        Assert.Equal(first.PlannedArrivalUtc.ToLocalTime().ToString("HH:mm"), trip.Rows[0].Time);
        Assert.True(trip.Rows[0].Done);
        Assert.Equal("был " + first.ArrivedAtUtc!.Value.ToLocalTime().ToString("HH:mm"), trip.Rows[0].Status);
        Assert.True(trip.Rows[1].IsNext);
        Assert.Equal("сейчас", trip.Rows[1].Status);
        Assert.False(trip.Rows[2].Done);
        Assert.Equal("", trip.Rows[2].Status);
    }

    [Fact]
    public void FromStops_all_visited_is_finished()
    {
        var trip = DriveItinerary.FromStops([Stop(1, "А", arrived: true, skipped: true)]);

        Assert.Null(trip.Next);
        Assert.Equal("Рейс завершён", trip.NextTitle);
        Assert.Equal("Все остановки отмечены", trip.NextMeta);
        Assert.Equal("пропущена", trip.Rows[0].Status);
    }

    [Fact]
    public void Subtitle_uses_operator_and_route()
    {
        var van = new VehicleDto(
            Guid.NewGuid(),
            "1234 AB-7",
            "Райпо",
            true,
            null,
            null,
            null,
            null);
        var route = new RouteDto(Guid.NewGuid(), "Восток", van.Id, []);

        Assert.Equal("Назначьте автолавку в админке", DriveItinerary.Subtitle(null, null));
        Assert.Equal("Райпо", DriveItinerary.Subtitle(van, null));
        Assert.Equal("Райпо · Восток", DriveItinerary.Subtitle(van, route));
    }

    [Fact]
    public void DelayMeta_stays_plan_until_one_minute_late()
    {
        var stop = Stop(1, "А");
        var onTime = DriveItinerary.DelayMeta(stop, stop.PlannedArrivalUtc.AddSeconds(30));
        var late = DriveItinerary.DelayMeta(stop, stop.PlannedArrivalUtc.AddMinutes(12));

        Assert.Equal(stop.PlannedArrivalUtc.ToLocalTime().ToString("HH:mm"), onTime);
        Assert.Contains("опоздание 12 мин", late, StringComparison.Ordinal);
    }

    private static RouteStopDto Stop(int seq, string name, bool arrived = false, bool skipped = false)
    {
        var plan = new DateTimeOffset(2026, 8, 17, 10, seq, 0, TimeSpan.Zero);
        return new RouteStopDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            seq,
            name,
            "BY-MI",
            53.5,
            27.5,
            plan,
            ArrivedAtUtc: arrived ? plan.AddMinutes(5) : null,
            Skipped: skipped);
    }
}
