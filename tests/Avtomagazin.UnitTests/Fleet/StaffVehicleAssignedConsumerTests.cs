using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Consumers;
using Avtomagazin.Fleet.Api.Data;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.UnitTests.Fleet;

public class StaffVehicleAssignedConsumerTests
{
    [Fact]
    public async Task Consume_moves_driver_off_old_van_onto_new()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var oldVan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var newVan = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<FleetDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StaffVehicleAssignedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.AddRange(
                new Vehicle
                {
                    Id = oldVan,
                    PlateNumber = "1234 AB-7",
                    OperatorName = "Гродно",
                    DriverUserId = userId,
                    DriverName = "Старое",
                    DriverPhone = "111",
                    IsActive = true
                },
                new Vehicle
                {
                    Id = newVan,
                    PlateNumber = "5678 CD-4",
                    OperatorName = "Пуховичи",
                    IsActive = true
                });
            await db.SaveChangesAsync();

            await harness.Bus.Publish(new StaffVehicleAssigned(
                userId,
                Roles.Driver,
                newVan,
                "Иван Новый",
                "+375291112233"));
        }

        Assert.True(await harness.Consumed.Any<StaffVehicleAssigned>());

        await using var assertScope = sp.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var grodno = await assertDb.Vehicles.AsNoTracking().SingleAsync(v => v.Id == oldVan);
        Assert.Null(grodno.DriverUserId);
        Assert.Null(grodno.DriverName);
        Assert.Null(grodno.DriverPhone);

        var pukhovichi = await assertDb.Vehicles.AsNoTracking().SingleAsync(v => v.Id == newVan);
        Assert.Equal(userId, pukhovichi.DriverUserId);
        Assert.Equal("Иван Новый", pukhovichi.DriverName);
        Assert.Equal("+375291112233", pukhovichi.DriverPhone);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_clears_driver_when_vehicle_is_null()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var vanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<FleetDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StaffVehicleAssignedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.Add(new Vehicle
            {
                Id = vanId,
                PlateNumber = "1234 AB-7",
                OperatorName = "Гродно",
                DriverUserId = userId,
                DriverName = "Иван",
                DriverPhone = "111",
                IsActive = true
            });
            await db.SaveChangesAsync();

            await harness.Bus.Publish(new StaffVehicleAssigned(
                userId,
                Roles.Driver,
                null,
                "Иван",
                "111"));
        }

        Assert.True(await harness.Consumed.Any<StaffVehicleAssigned>());

        await using var assertScope = sp.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var van = await assertDb.Vehicles.AsNoTracking().SingleAsync(v => v.Id == vanId);
        Assert.Null(van.DriverUserId);
        Assert.Null(van.DriverName);
        Assert.Null(van.DriverPhone);

        await harness.Stop();
    }
}
