using Avtomagazin.Notifications.Api.Data;
using Avtomagazin.Notifications.Api.Push;

namespace Avtomagazin.Notifications.Api;

internal static class FavoriteStopNotifier
{
    public static async Task<int> NotifyAsync(
        NotificationsDbContext db,
        IPushSender pushSender,
        Guid stopId,
        string settlementName,
        string title,
        string body,
        CancellationToken ct)
    {
        var tokens = await FavoritePush.TokensForStopAsync(db, stopId, settlementName, ct);
        foreach (var token in tokens)
        {
            await pushSender.SendAsync(token, title, body, ct);
        }

        db.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            Title = title,
            Body = body,
            SettlementName = settlementName,
            SentAtUtc = DateTimeOffset.UtcNow,
            RecipientCount = tokens.Count
        });

        await db.SaveChangesAsync(ct);
        return tokens.Count;
    }
}
