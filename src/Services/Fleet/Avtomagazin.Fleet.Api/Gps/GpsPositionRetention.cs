using Avtomagazin.Fleet.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Gps;

public static class GpsPositionRetention
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    public static async Task<int> PurgeAsync(FleetDbContext db, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - KeepFor;
        if (db.Database.IsRelational())
        {
            return await db.Positions
                .Where(p => p.RecordedAtUtc < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        var stale = await db.Positions
            .Where(p => p.RecordedAtUtc < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0)
        {
            return 0;
        }

        db.Positions.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
