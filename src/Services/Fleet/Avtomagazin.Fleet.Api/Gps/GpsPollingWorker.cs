using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using Avtomagazin.Fleet.Api.Gps;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Gps;

public sealed class GpsPollingWorker(
    IServiceScopeFactory scopeFactory,
    IGpsProvider gpsProvider,
    ILogger<GpsPollingWorker> logger) : BackgroundService
{
    private int _cycles;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GPS polling worker started (provider={Provider})", gpsProvider.GetType().Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var fixes = await gpsProvider.PollAsync(stoppingToken);
                if (fixes.Count > 0)
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
                    var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                    var ids = fixes.Select(f => f.VehicleId).Distinct().ToList();
                    var vehicles = await db.Vehicles
                        .Where(v => ids.Contains(v.Id))
                        .ToDictionaryAsync(v => v.Id, stoppingToken);

                    foreach (var fix in fixes)
                    {
                        if (!vehicles.TryGetValue(fix.VehicleId, out var vehicle))
                        {
                            continue;
                        }

                        // Live broadcast from driver/seller app wins over mock telematics.
                        var live = string.Equals(vehicle.LastSource, "driver-app", StringComparison.OrdinalIgnoreCase)
                                   && vehicle.LastSeenAtUtc > DateTimeOffset.UtcNow.AddSeconds(-90);
                        if (live)
                        {
                            continue;
                        }

                        var position = new VehiclePosition
                        {
                            Id = Guid.NewGuid(),
                            VehicleId = fix.VehicleId,
                            Latitude = fix.Latitude,
                            Longitude = fix.Longitude,
                            SpeedKmh = fix.SpeedKmh,
                            RecordedAtUtc = fix.RecordedAtUtc,
                            Source = "gps-adapter"
                        };

                        vehicle.LastLatitude = fix.Latitude;
                        vehicle.LastLongitude = fix.Longitude;
                        vehicle.LastSeenAtUtc = fix.RecordedAtUtc;
                        vehicle.LastSource = "gps-adapter";
                        db.Positions.Add(position);

                        await bus.Publish(new VehiclePositionUpdated(
                            vehicle.Id,
                            vehicle.PlateNumber,
                            fix.Latitude,
                            fix.Longitude,
                            fix.SpeedKmh,
                            fix.RecordedAtUtc), stoppingToken);
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    if (++_cycles % 20 == 0 && db.Database.IsRelational())
                    {
                        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                        await db.Positions
                            .Where(p => p.RecordedAtUtc < cutoff)
                            .ExecuteDeleteAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "GPS poll cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
