using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using MassTransit;

namespace Avtomagazin.Notifications.Api.Consumers;

public sealed class StopArrivalEstimatedConsumer(
    NotificationsDbContext db,
    IPushSender pushSender,
    ILogger<StopArrivalEstimatedConsumer> logger) : IConsumer<StopArrivalEstimated>
{
    public async Task Consume(ConsumeContext<StopArrivalEstimated> context)
    {
        var msg = context.Message;
        var count = await FavoriteStopNotifier.NotifyAsync(
            db,
            pushSender,
            msg.StopId,
            msg.SettlementName,
            "Автолавка скоро будет",
            $"{msg.SettlementName}: примерно через {msg.MinutesUntilArrival} мин.",
            context.CancellationToken);

        logger.LogInformation(
            "ETA alert for {Settlement} ({Minutes} min): push to {Count} devices",
            msg.SettlementName,
            msg.MinutesUntilArrival,
            count);
    }
}
