using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Consumers;
using Avtomagazin.Routing.Api.Data;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.UnitTests.Routing;

public class VehiclePositionUpdatedConsumerTests
{
    private static readonly Guid VehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RouteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid StopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");

    [Fact]
    public async Task Consume_writes_eta_without_alert_when_far()
    {
        await using var sp = await BuildAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await SeedRouteAsync(sp, stopLat: 53.46, stopLng: 23.95);
        await PublishPositionAsync(harness, lat: 53.6694, lng: 23.8131, speed: 40);

        Assert.True(await harness.Consumed.Any<VehiclePositionUpdated>());
        Assert.False(await harness.Published.Any<StopArrivalEstimated>());

        var eta = await SingleEtaAsync(sp);
        Assert.Equal("Индура", eta.SettlementName);
        Assert.True(eta.MinutesUntilArrival > 15);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_publishes_StopArrivalEstimated_when_entering_near_window()
    {
        await using var sp = await BuildAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Stop ~1.3 km away at 40 km/h ≈ 2 minutes.
        await SeedRouteAsync(sp, stopLat: 53.58, stopLng: 27.76);
        await PublishPositionAsync(harness, lat: 53.577, lng: 27.747, speed: 40);

        Assert.True(await harness.Consumed.Any<VehiclePositionUpdated>());
        Assert.True(await harness.Published.Any<StopArrivalEstimated>());

        var eta = await SingleEtaAsync(sp);
        Assert.True(eta.MinutesUntilArrival <= 15);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_does_not_republish_while_still_near_same_stop()
    {
        await using var sp = await BuildAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await SeedRouteAsync(sp, stopLat: 53.58, stopLng: 27.76);
        await PublishPositionAsync(harness, lat: 53.577, lng: 27.747, speed: 40);
        Assert.True(await harness.Published.Any<StopArrivalEstimated>());

        var publishedBefore = harness.Published.Select<StopArrivalEstimated>().Count();
        await PublishPositionAsync(harness, lat: 53.5772, lng: 27.7472, speed: 35);

        await Task.Delay(300);
        Assert.Equal(publishedBefore, harness.Published.Select<StopArrivalEstimated>().Count());

        await harness.Stop();
    }

    private static async Task<ServiceProvider> BuildAsync()
    {
        var dbName = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddLogging()
            .AddDbContext<RoutingDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<VehiclePositionUpdatedConsumer>())
            .BuildServiceProvider(true);
    }

    private static async Task SeedRouteAsync(ServiceProvider sp, double stopLat, double stopLng)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
        db.Routes.Add(new TradeRoute
        {
            Id = RouteId,
            Name = "Test",
            VehicleId = VehicleId,
            Stops =
            [
                new RouteStop
                {
                    Id = StopId,
                    RouteId = RouteId,
                    Sequence = 1,
                    SettlementName = "Индура",
                    RegionCode = "BY-HR",
                    Latitude = stopLat,
                    Longitude = stopLng,
                    PlannedArrivalUtc = DateTimeOffset.UtcNow.AddHours(1)
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static Task PublishPositionAsync(ITestHarness harness, double lat, double lng, double speed)
        => harness.Bus.Publish(new VehiclePositionUpdated(
            VehicleId,
            "1234 AB-7",
            lat,
            lng,
            speed,
            DateTimeOffset.UtcNow));

    private static async Task<EtaSnapshot> SingleEtaAsync(ServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
        var etas = await db.EtaSnapshots.AsNoTracking().ToListAsync();
        return Assert.Single(etas);
    }
}
