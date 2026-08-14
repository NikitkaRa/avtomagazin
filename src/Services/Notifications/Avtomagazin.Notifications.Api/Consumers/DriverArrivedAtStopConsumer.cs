using Avtomagazin.Contracts.Events;
using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;
using MassTransit;
using Microsoft.EntityFrameworkCore;

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

        var favoriteTokens = await db.FavoriteStops
            .AsNoTracking()
            .Where(f => f.StopId == msg.StopId)
            .Select(f => f.Device.DeviceToken)
            .ToListAsync(context.CancellationToken);

        var legacyTokens = await db.DeviceSubscriptions
            .AsNoTracking()
            .Where(d => d.SettlementName == msg.SettlementName)
            .Select(d => d.DeviceToken)
            .ToListAsync(context.CancellationToken);

        var tokens = favoriteTokens.Concat(legacyTokens).Distinct().ToList();

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
            "Driver arrived at {Settlement}: push to {Count} favorite devices",
            msg.SettlementName,
            tokens.Count);
    }
}
