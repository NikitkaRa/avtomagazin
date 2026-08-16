using Avtomagazin.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Notifications.Api.Data;

internal static class HeatSeed
{
    public static async Task EnsureAsync(NotificationsDbContext db)
    {
        var markerStop = DemoHeatCatalog.StopId(0);
        if (await db.FavoriteStops.AnyAsync(f => f.StopId == markerStop && f.UserId != null))
        {
            return;
        }

        // подчистить недописанный прошлый прогон
        await db.FavoriteStops.Where(f => f.Device.Platform == "seed").ExecuteDeleteAsync();
        await db.DeviceSubscriptions.Where(d => d.Platform == "seed").ExecuteDeleteAsync();

        var now = DateTimeOffset.UtcNow;
        var places = DemoHeatCatalog.Places;

        var devices = new List<DeviceSubscription>(DemoHeatCatalog.ResidentCount);
        for (var i = 0; i < DemoHeatCatalog.ResidentCount; i++)
        {
            devices.Add(new DeviceSubscription
            {
                Id = DemoHeatCatalog.DeviceId(i),
                UserId = DemoHeatCatalog.ResidentId(i),
                DeviceToken = $"heat-device-{i}",
                Platform = "seed",
                SettlementName = places[i % places.Count].Name,
                CreatedAtUtc = now
            });
        }

        db.DeviceSubscriptions.AddRange(devices);
        await db.SaveChangesAsync();

        var favorites = new List<FavoriteStop>(8192);
        for (var stopIndex = 0; stopIndex < places.Count; stopIndex++)
        {
            var stopId = DemoHeatCatalog.StopId(stopIndex);
            var name = places[stopIndex].Name;
            var target = DemoHeatCatalog.TargetSubscribers(stopIndex, places.Count);
            if (target <= 0)
            {
                continue;
            }

            var offset = (stopIndex * 37) % DemoHeatCatalog.ResidentCount;
            for (var n = 0; n < target; n++)
            {
                var userIndex = (offset + n) % DemoHeatCatalog.ResidentCount;
                favorites.Add(new FavoriteStop
                {
                    Id = Guid.NewGuid(),
                    UserId = DemoHeatCatalog.ResidentId(userIndex),
                    DeviceSubscriptionId = DemoHeatCatalog.DeviceId(userIndex),
                    StopId = stopId,
                    SettlementName = name,
                    CreatedAtUtc = now
                });
            }

            if (favorites.Count >= 2000)
            {
                db.FavoriteStops.AddRange(favorites);
                await db.SaveChangesAsync();
                favorites.Clear();
            }
        }

        if (favorites.Count > 0)
        {
            db.FavoriteStops.AddRange(favorites);
            await db.SaveChangesAsync();
        }
    }
}
