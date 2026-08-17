using Avtomagazin.Routing.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.UnitTests.Routing;

public class SeedTests
{
    [Fact]
    public async Task EnsureSeed_adds_pukhovichi_route_with_ozerichino()
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        await Seed.EnsureSeedAsync(db);
        await Seed.EnsureSeedAsync(db);

        var names = await db.Stops.AsNoTracking().Select(s => s.SettlementName).ToListAsync();
        Assert.Contains("Индура", names);
        Assert.Contains("Озеричино", names);
        Assert.Contains("Правдинский", names);
        Assert.Contains("Дукора", names);

        var home = Assert.Single(db.Stops.Where(s => s.SettlementName == "Озеричино"));
        Assert.InRange(home.Latitude, 53.56, 53.59);
        Assert.InRange(home.Longitude, 27.73, 27.76);

        var route = await db.Routes.AsNoTracking().SingleAsync(r => r.Id == home.RouteId);
        Assert.Equal("Пуховичи — Озеричино", route.Name);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), route.VehicleId);

        var grodno = await db.Routes.AsNoTracking()
            .Include(r => r.Stops)
            .SingleAsync(r => r.Name.Contains("Гродно"));
        Assert.DoesNotContain(grodno.Stops, s => s.SettlementName == "Озеричино");
    }

    [Fact]
    public async Task EnsureSeed_does_not_steal_existing_stop()
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        var grodnoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var stopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd4");
        db.Routes.Add(new TradeRoute
        {
            Id = grodnoId,
            Name = "Гродно — окраинные деревни",
            VehicleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Stops =
            [
                new RouteStop
                {
                    Id = stopId,
                    RouteId = grodnoId,
                    Sequence = 4,
                    SettlementName = "Озеричино",
                    RegionCode = "BY-MI",
                    Latitude = 53.5774,
                    Longitude = 27.7472,
                    PlannedArrivalUtc = DateTimeOffset.UtcNow
                }
            ]
        });
        await db.SaveChangesAsync();

        await Seed.EnsureSeedAsync(db);

        var home = await db.Stops.AsNoTracking().SingleAsync(s => s.Id == stopId);
        Assert.Equal(grodnoId, home.RouteId);
        Assert.Equal("Озеричино", home.SettlementName);
    }

    [Fact]
    public async Task Ozerichino_is_nearest_stop_from_village_coords()
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        await Seed.EnsureSeedAsync(db);

        const double lat = 53.5774;
        const double lng = 27.7472;
        var nearest = db.Stops.AsEnumerable()
            .OrderBy(s => Avtomagazin.Routing.Api.NextStop.DistanceKm(lat, lng, s.Latitude, s.Longitude))
            .First();

        Assert.Equal("Озеричино", nearest.SettlementName);
        Assert.True(Avtomagazin.Routing.Api.NextStop.DistanceKm(lat, lng, nearest.Latitude, nearest.Longitude) < 0.05);
    }
}
