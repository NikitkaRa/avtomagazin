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

                    await GpsFixApplier.ApplyAsync(db, bus, fixes, stoppingToken);

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
