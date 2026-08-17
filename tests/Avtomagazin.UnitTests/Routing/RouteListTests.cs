using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api;
using Avtomagazin.Routing.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.UnitTests.Routing;

public class RouteListTests
{
    [Fact]
    public async Task Build_default_returns_only_today_stops_and_excludes_heat()
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        await Seed.EnsureSeedAsync(db);

        db.Stops.Add(new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Sequence = 99,
            SettlementName = "Вчерашняя",
            RegionCode = "BY-HR",
            Latitude = 53.5,
            Longitude = 24,
            PlannedArrivalUtc = DateTimeOffset.UtcNow.AddDays(-3)
        });
        await db.SaveChangesAsync();

        var items = await RouteList.BuildAsync(db, catalog: false, CancellationToken.None);
        var names = items.SelectMany(r => r.Stops ?? []).Select(s => s.SettlementName).ToList();

        Assert.Contains("Индура", names);
        Assert.DoesNotContain("Вчерашняя", names);
        Assert.DoesNotContain(items, r => r.Id == DemoHeatCatalog.HeatRouteId);
        Assert.DoesNotContain(items, r => r.Name.Contains("избранное", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_default_excludes_any_catalog_route()
    {
        var extraCatalog = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        await Seed.EnsureSeedAsync(db);
        db.Routes.Add(new TradeRoute
        {
            Id = extraCatalog,
            Name = "Каталог не GUID тепла",
            VehicleId = Guid.NewGuid(),
            IsCatalog = true
        });
        await db.SaveChangesAsync();

        var items = await RouteList.BuildAsync(db, catalog: false, CancellationToken.None);
        Assert.DoesNotContain(items, r => r.Id == extraCatalog);
        Assert.DoesNotContain(items, r => r.Id == DemoHeatCatalog.HeatRouteId);
    }

    [Fact]
    public async Task Build_catalog_includes_heat()
    {
        var options = new DbContextOptionsBuilder<RoutingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new RoutingDbContext(options);
        await Seed.EnsureSeedAsync(db);

        var items = await RouteList.BuildAsync(db, catalog: true, CancellationToken.None);
        Assert.Contains(items, r => r.Id == DemoHeatCatalog.HeatRouteId);
    }
}
