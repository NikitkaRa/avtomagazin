using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Consumers;
using Avtomagazin.Fleet.Api.Data;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.UnitTests.Fleet;

public class StaffContactChangedConsumerTests
{
    [Fact]
    public async Task Consume_updates_driver_and_seller_snapshots_on_assigned_vans()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var otherUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddDbContext<FleetDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StaffContactChangedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.AddRange(
                new Vehicle
                {
                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    PlateNumber = "1234 AB-7",
                    OperatorName = "Гродно",
                    DriverUserId = userId,
                    DriverName = "Старое",
                    DriverPhone = "111",
                    IsActive = true
                },
                new Vehicle
                {
                    Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    PlateNumber = "5678 CD-4",
                    OperatorName = "Пуховичи",
                    SellerUserId = userId,
                    SellerName = "Старое",
                    SellerPhone = "111",
                    DriverUserId = otherUser,
                    DriverName = "Другой",
                    IsActive = true
                },
                new Vehicle
                {
                    Id = Guid.NewGuid(),
                    PlateNumber = "9999 ZZ-1",
                    OperatorName = "Чужой",
                    DriverUserId = otherUser,
                    DriverName = "Не трогать",
                    IsActive = true
                });
            await db.SaveChangesAsync();

            await harness.Bus.Publish(new StaffContactChanged(userId, "Иван Новый", "+375291112233"));
        }

        Assert.True(await harness.Consumed.Any<StaffContactChanged>());

        await using var assertScope = sp.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var vans = await assertDb.Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).ToListAsync();

        var grodno = Assert.Single(vans, v => v.PlateNumber == "1234 AB-7");
        Assert.Equal("Иван Новый", grodno.DriverName);
        Assert.Equal("+375291112233", grodno.DriverPhone);

        var pukhovichi = Assert.Single(vans, v => v.PlateNumber == "5678 CD-4");
        Assert.Equal("Иван Новый", pukhovichi.SellerName);
        Assert.Equal("+375291112233", pukhovichi.SellerPhone);
        Assert.Equal("Другой", pukhovichi.DriverName);

        var other = Assert.Single(vans, v => v.PlateNumber == "9999 ZZ-1");
        Assert.Equal("Не трогать", other.DriverName);

        await harness.Stop();
    }
}
