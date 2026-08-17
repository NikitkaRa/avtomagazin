using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Gps;

public static class GpsFixApplier
{
    public static async Task<int> ApplyAsync(
        FleetDbContext db,
        IPublishEndpoint bus,
        IReadOnlyList<GpsFix> fixes,
        CancellationToken cancellationToken)
    {
        if (fixes.Count == 0)
        {
            return 0;
        }

        var ids = fixes.Select(f => f.VehicleId).Distinct().ToList();
        var vehicles = await db.Vehicles
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var applied = 0;
        foreach (var fix in fixes)
        {
            if (!vehicles.TryGetValue(fix.VehicleId, out var vehicle))
            {
                continue;
            }

            // Live broadcast from driver/seller app wins over mock telematics.
            var live = string.Equals(vehicle.LastSource, GpsSources.DriverApp, StringComparison.OrdinalIgnoreCase)
                       && vehicle.LastSeenAtUtc > DateTimeOffset.UtcNow.AddSeconds(-90);
            if (live)
            {
                continue;
            }

            db.Positions.Add(new VehiclePosition
            {
                Id = Guid.NewGuid(),
                VehicleId = fix.VehicleId,
                Latitude = fix.Latitude,
                Longitude = fix.Longitude,
                SpeedKmh = fix.SpeedKmh,
                RecordedAtUtc = fix.RecordedAtUtc,
                Source = GpsSources.Adapter
            });

            vehicle.LastLatitude = fix.Latitude;
            vehicle.LastLongitude = fix.Longitude;
            vehicle.LastSeenAtUtc = fix.RecordedAtUtc;
            vehicle.LastSource = GpsSources.Adapter;
            applied++;

            await bus.Publish(new VehiclePositionUpdated(
                vehicle.Id,
                vehicle.PlateNumber,
                fix.Latitude,
                fix.Longitude,
                fix.SpeedKmh,
                fix.RecordedAtUtc), cancellationToken);
        }

        if (applied > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return applied;
    }
}
