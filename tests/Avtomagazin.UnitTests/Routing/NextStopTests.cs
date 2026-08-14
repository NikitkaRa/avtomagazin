using Avtomagazin.Routing.Api;
using Avtomagazin.Routing.Api.Data;

namespace Avtomagazin.UnitTests.Routing;

public class NextStopTests
{
    [Fact]
    public void Pick_uses_sequence_not_raw_distance_when_later_stop_is_closer()
    {
        var stops = new[]
        {
            Stop(1, "Правдинский", 53.5085, 27.8372),
            Stop(2, "Озеричино", 53.5774, 27.7472),
            Stop(3, "Дукора", 53.6786, 27.9525)
        };

        var next = NextStop.Pick(stops, 53.50, 27.82);

        Assert.Equal("Правдинский", next.SettlementName);
    }

    [Fact]
    public void Pick_advances_when_already_at_current_stop()
    {
        var stops = new[]
        {
            Stop(1, "Правдинский", 53.5085, 27.8372),
            Stop(2, "Озеричино", 53.5774, 27.7472)
        };

        var next = NextStop.Pick(stops, 53.5085, 27.8372);

        Assert.Equal("Озеричино", next.SettlementName);
    }

    private static RouteStop Stop(int sequence, string name, double lat, double lng) => new()
    {
        Id = Guid.NewGuid(),
        RouteId = Guid.NewGuid(),
        Sequence = sequence,
        SettlementName = name,
        RegionCode = "BY-MI",
        Latitude = lat,
        Longitude = lng,
        PlannedArrivalUtc = DateTimeOffset.UtcNow
    };
}
