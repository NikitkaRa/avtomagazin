using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.Fleet.Api.Gps;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.UnitTests.Gps;

public class GpsFixApplierTests
{
    [Fact]
    public async Task Apply_updates_batch_and_publishes_events()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var b = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<FleetDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(_ => { })
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.AddRange(
                new Vehicle { Id = a, PlateNumber = "1234 AB-7", OperatorName = "A", IsActive = true },
                new Vehicle { Id = b, PlateNumber = "5678 CD-4", OperatorName = "B", IsActive = true });
            await db.SaveChangesAsync();

            var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var applied = await GpsFixApplier.ApplyAsync(
                db,
                bus,
                [
                    new GpsFix(a, 53.1, 23.1, 30, DateTimeOffset.UtcNow),
                    new GpsFix(b, 53.5, 27.7, 25, DateTimeOffset.UtcNow)
                ],
                CancellationToken.None);

            Assert.Equal(2, applied);
        }

        Assert.True(await harness.Published.Any<VehiclePositionUpdated>());
        Assert.Equal(2, harness.Published.Select<VehiclePositionUpdated>().Count());

        await using var assert = sp.CreateAsyncScope();
        var vans = await assert.ServiceProvider.GetRequiredService<FleetDbContext>()
            .Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).ToListAsync();
        Assert.All(vans, v => Assert.Equal(GpsSources.Adapter, v.LastSource));
        Assert.Equal(2, await assert.ServiceProvider.GetRequiredService<FleetDbContext>().Positions.CountAsync());

        await harness.Stop();
    }

    [Fact]
    public async Task Apply_skips_fresh_driver_app_broadcast()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<FleetDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(_ => { })
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.Add(new Vehicle
            {
                Id = a,
                PlateNumber = "1234 AB-7",
                OperatorName = "A",
                IsActive = true,
                LastSource = GpsSources.DriverApp,
                LastLatitude = 53.0,
                LastLongitude = 23.0,
                LastSeenAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10)
            });
            await db.SaveChangesAsync();

            var applied = await GpsFixApplier.ApplyAsync(
                db,
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                [new GpsFix(a, 99, 99, 10, DateTimeOffset.UtcNow)],
                CancellationToken.None);

            Assert.Equal(0, applied);
        }

        Assert.False(await harness.Published.Any<VehiclePositionUpdated>());

        await using var assert = sp.CreateAsyncScope();
        var van = await assert.ServiceProvider.GetRequiredService<FleetDbContext>()
            .Vehicles.AsNoTracking().SingleAsync();
        Assert.Equal(GpsSources.DriverApp, van.LastSource);
        Assert.Equal(53.0, van.LastLatitude);
        Assert.Empty(await assert.ServiceProvider.GetRequiredService<FleetDbContext>().Positions.ToListAsync());

        await harness.Stop();
    }
}
