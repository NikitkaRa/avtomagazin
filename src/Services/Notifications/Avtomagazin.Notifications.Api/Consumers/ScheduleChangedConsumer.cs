using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using MassTransit;

namespace Avtomagazin.Notifications.Api.Consumers;

public sealed class ScheduleChangedConsumer(
    NotificationsDbContext db,
    IPushSender pushSender) : IConsumer<ScheduleChanged>
{
    public async Task Consume(ConsumeContext<ScheduleChanged> context)
    {
        var msg = context.Message;
        var title = "Изменение расписания автолавки";
        var body = $"{msg.SettlementName}: {msg.Reason}";
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
    }
}
