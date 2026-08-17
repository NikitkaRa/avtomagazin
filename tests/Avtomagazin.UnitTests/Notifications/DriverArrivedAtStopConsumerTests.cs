using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api.Consumers;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Avtomagazin.UnitTests.Notifications;

public class DriverArrivedAtStopConsumerTests
{
    [Fact]
    public async Task Consume_sends_push_only_to_stop_favorites_not_settlement_name()
    {
        var dbName = Guid.NewGuid().ToString();
        var push = Substitute.For<IPushSender>();
        var stopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(push)
            .AddDbContext<NotificationsDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<DriverArrivedAtStopConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var favoriteDevice = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                DeviceToken = "device-fav",
                Platform = "web",
                SettlementName = "Озёры",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var settlementOnly = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                DeviceToken = "device-settlement",
                Platform = "ios",
                SettlementName = "Индура",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var other = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                DeviceToken = "device-other",
                Platform = "android",
                SettlementName = "Скидель",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.DeviceSubscriptions.AddRange(favoriteDevice, settlementOnly, other);
            favoriteDevice.Favorites.Add(new FavoriteStop
            {
                Id = Guid.NewGuid(),
                DeviceSubscriptionId = favoriteDevice.Id,
                Device = favoriteDevice,
                StopId = stopId,
                SettlementName = "Индура",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await harness.Bus.Publish(new DriverArrivedAtStop(
            Guid.NewGuid(),
            Guid.NewGuid(),
            stopId,
            "Индура",
            DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<DriverArrivedAtStop>());
        await push.Received(1).SendAsync(
            "device-fav",
            Arg.Any<string>(),
            Arg.Is<string>(b => b.Contains("Индура")),
            Arg.Any<CancellationToken>());
        await push.DidNotReceive().SendAsync(
            "device-settlement",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await push.DidNotReceive().SendAsync(
            "device-other",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await using var assertScope = sp.CreateAsyncScope();
        var logs = await assertScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .NotificationLogs.AsNoTracking()
            .ToListAsync();
        Assert.Contains(logs, l => l.SettlementName == "Индура" && l.RecipientCount == 1);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_pushes_every_device_of_the_subscribed_user()
    {
        var dbName = Guid.NewGuid().ToString();
        var push = Substitute.For<IPushSender>();
        var stopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd2");
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(push)
            .AddDbContext<NotificationsDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<DriverArrivedAtStopConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var phone = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceToken = "user-phone",
                Platform = "android",
                SettlementName = "Озеричино",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var web = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceToken = "user-web",
                Platform = "web",
                SettlementName = "Озеричино",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.DeviceSubscriptions.AddRange(phone, web);
            phone.Favorites.Add(new FavoriteStop
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceSubscriptionId = phone.Id,
                Device = phone,
                StopId = stopId,
                SettlementName = "Озеричино",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await harness.Bus.Publish(new DriverArrivedAtStop(
            Guid.NewGuid(),
            Guid.NewGuid(),
            stopId,
            "Озеричино",
            DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<DriverArrivedAtStop>());
        await push.Received(1).SendAsync("user-phone", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await push.Received(1).SendAsync("user-web", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness.Stop();
    }
}
