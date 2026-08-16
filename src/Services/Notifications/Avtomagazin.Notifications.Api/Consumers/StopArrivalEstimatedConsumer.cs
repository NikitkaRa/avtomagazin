using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api;
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
        var title = "Автолавка скоро будет";
        var body = $"{msg.SettlementName}: примерно через {msg.MinutesUntilArrival} мин.";
        var tokens = await FavoritePush.TokensForStopAsync(
            db,
            msg.StopId,
            msg.SettlementName,
            context.CancellationToken);

        foreach (var token in tokens)
        {
            await pushSender.SendAsync(token, title, body, context.CancellationToken);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            Title = title,
            Body = body,
            SettlementName = msg.SettlementName,
            SentAtUtc = DateTimeOffset.UtcNow,
            RecipientCount = tokens.Count
        });

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "ETA alert for {Settlement} ({Minutes} min): push to {Count} devices",
            msg.SettlementName,
            msg.MinutesUntilArrival,
            tokens.Count);
    }
}
