using Avtomagazin.Fleet.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Gps;

public sealed class PositionRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PositionRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
                if (db.Database.IsRelational())
                {
                    var removed = await GpsPositionRetention.PurgeAsync(db, stoppingToken);
                    if (removed > 0)
                    {
                        logger.LogInformation("Purged {Count} GPS points older than {Keep}", removed, GpsPositionRetention.KeepFor);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "GPS position retention failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
