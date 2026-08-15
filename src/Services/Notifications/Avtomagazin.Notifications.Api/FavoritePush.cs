using Avtomagazin.Notifications.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api;

internal static class FavoritePush
{
    public static async Task<List<string>> TokensForStopAsync(
        NotificationsDbContext db,
        Guid stopId,
        string settlementName,
        CancellationToken ct)
    {
        var userIds = await db.FavoriteStops
            .AsNoTracking()
            .Where(f => f.StopId == stopId && f.UserId != null)
            .Select(f => f.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var userTokens = userIds.Count == 0
            ? []
            : await db.DeviceSubscriptions
                .AsNoTracking()
                .Where(d => d.UserId != null && userIds.Contains(d.UserId.Value))
                .Select(d => d.DeviceToken)
                .ToListAsync(ct);

        var favoriteDeviceTokens = await db.FavoriteStops
            .AsNoTracking()
            .Where(f => f.StopId == stopId)
            .Select(f => f.Device.DeviceToken)
            .ToListAsync(ct);

        var legacyTokens = await db.DeviceSubscriptions
            .AsNoTracking()
            .Where(d => d.SettlementName == settlementName)
            .Select(d => d.DeviceToken)
            .ToListAsync(ct);

        return userTokens
            .Concat(favoriteDeviceTokens)
            .Concat(legacyTokens)
            .Distinct()
            .ToList();
    }
}
