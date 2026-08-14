using Avtomagazin.Fleet.Api.Gps;

namespace Avtomagazin.UnitTests;

public class MockGpsProviderTests
{
    [Fact]
    public async Task PollAsync_returns_fixes_for_seeded_vehicles()
    {
        var provider = new MockGpsProvider();

        var fixes = await provider.PollAsync(CancellationToken.None);

        Assert.Equal(2, fixes.Count);
        Assert.Equal(2, fixes.Select(f => f.VehicleId).Distinct().Count());
        Assert.All(fixes, f =>
        {
            Assert.InRange(f.Latitude, 50, 56);
            Assert.InRange(f.Longitude, 20, 30);
            Assert.True(f.SpeedKmh > 0);
        });
    }

    [Fact]
    public async Task PollAsync_moves_positions_over_time()
    {
        var provider = new MockGpsProvider();

        var first = await provider.PollAsync(CancellationToken.None);
        var second = await provider.PollAsync(CancellationToken.None);

        Assert.NotEqual(first[0].Latitude, second[0].Latitude);
        Assert.NotEqual(first[0].Longitude, second[0].Longitude);
    }
}
