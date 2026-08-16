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

public class StopArrivalEstimatedConsumerTests
{
    [Fact]
    public async Task Consume_sends_push_to_favorite_devices()
    {
        var dbName = Guid.NewGuid().ToString();
        var push = Substitute.For<IPushSender>();
        var stopId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(push)
            .AddDbContext<NotificationsDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StopArrivalEstimatedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var device = new DeviceSubscription
            {
                Id = Guid.NewGuid(),
                DeviceToken = "eta-device",
                Platform = "android",
                SettlementName = "Озёры",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.DeviceSubscriptions.Add(device);
            db.FavoriteStops.Add(new FavoriteStop
            {
                Id = Guid.NewGuid(),
                DeviceSubscriptionId = device.Id,
                Device = device,
                StopId = stopId,
                SettlementName = "Индура",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            await harness.Bus.Publish(new StopArrivalEstimated(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                stopId,
                "Индура",
                DateTimeOffset.UtcNow.AddMinutes(8),
                8));
        }

        Assert.True(await harness.Consumed.Any<StopArrivalEstimated>());
        await push.Received().SendAsync("eta-device", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var assertScope = sp.CreateAsyncScope();
        var logs = await assertScope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .NotificationLogs.AsNoTracking().ToListAsync();
        Assert.Contains(logs, l => l.SettlementName == "Индура" && l.RecipientCount >= 1);

        await harness.Stop();
    }
}
