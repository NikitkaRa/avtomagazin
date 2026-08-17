using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using MassTransit;

namespace Avtomagazin.Notifications.Api.Consumers;

public sealed class DriverArrivedAtStopConsumer(
    NotificationsDbContext db,
    IPushSender pushSender,
    ILogger<DriverArrivedAtStopConsumer> logger) : IConsumer<DriverArrivedAtStop>
{
    public async Task Consume(ConsumeContext<DriverArrivedAtStop> context)
    {
        var msg = context.Message;
        var title = "Автолавка на месте";
        var body = $"{msg.SettlementName}: уже можно подходить.";
        var count = await FavoriteStopNotifier.NotifyAsync(
            db,
            pushSender,
            msg.StopId,
            msg.SettlementName,
            title,
            body,
            context.CancellationToken);

        logger.LogInformation(
            "Driver arrived at {Settlement}: push to {Count} favorite devices",
            msg.SettlementName,
            count);
    }
}
