using Avtomagazin.Contracts.Events;
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
        await FavoriteStopNotifier.NotifyAsync(
            db,
            pushSender,
            msg.StopId,
            msg.SettlementName,
            "Изменение расписания автолавки",
            $"{msg.SettlementName}: {msg.Reason}",
            context.CancellationToken);
    }
}
