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
    [Fact]
    public async Task Consume_writes_eta_and_publishes_StopArrivalEstimated()
    {
        var dbName = Guid.NewGuid().ToString();

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<RoutingDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<VehiclePositionUpdatedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RoutingDbContext>();
            var vehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var routeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var stopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");

            db.Routes.Add(new TradeRoute
            {
                Id = routeId,
                Name = "Test",
                VehicleId = vehicleId,
                Stops =
                [
                    new RouteStop
                    {
                        Id = stopId,
                        RouteId = routeId,
                        Sequence = 1,
                        SettlementName = "Индура",
                        RegionCode = "BY-HR",
                        Latitude = 53.46,
                        Longitude = 23.95,
                        PlannedArrivalUtc = DateTimeOffset.UtcNow.AddHours(1)
                    }
                ]
            });
            await db.SaveChangesAsync();

            await harness.Bus.Publish(new VehiclePositionUpdated(
                vehicleId,
                "1234 AB-7",
                53.6694,
                23.8131,
                40,
                DateTimeOffset.UtcNow));
        }

        Assert.True(await harness.Consumed.Any<VehiclePositionUpdated>());
        Assert.True(await harness.Published.Any<StopArrivalEstimated>());

        await using var assertScope = sp.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<RoutingDbContext>();
        var etas = await assertDb.EtaSnapshots.AsNoTracking().ToListAsync();
        Assert.Single(etas);
        Assert.Equal("Индура", etas[0].SettlementName);
        Assert.True(etas[0].MinutesUntilArrival > 0);

        await harness.Stop();
    }
}
