using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api;

/// <summary>Service day in Europe/Minsk — operational trips are planned for a calendar day.</summary>
public static class ServiceDay
{
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) TodayBounds(DateTimeOffset? utcNow = null)
    {
        var start = BelarusTime.TodayAtLocalClock(utcNow ?? DateTimeOffset.UtcNow, 0, 0);
        return (start, start.AddDays(1));
    }
}

public static class RouteList
{
    public static async Task<List<RouteDto>> BuildAsync(RoutingDbContext db, bool catalog, CancellationToken ct)
    {
        var routes = await db.Routes.AsNoTracking()
            .Include(r => r.Stops)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        if (!catalog)
        {
            var (dayStart, dayEnd) = ServiceDay.TodayBounds();
            routes = routes
                .Where(r => !r.IsCatalog)
                .Select(r =>
                {
                    r.Stops = r.Stops
                        .Where(s => s.PlannedArrivalUtc >= dayStart && s.PlannedArrivalUtc < dayEnd)
                        .OrderBy(s => s.Sequence)
                        .ToList();
                    return r;
                })
                .Where(r => r.Stops.Count > 0)
                .ToList();
        }
        else
        {
            foreach (var r in routes)
            {
                r.Stops = r.Stops.OrderBy(s => s.Sequence).ToList();
            }
        }

        var stopIds = routes.SelectMany(r => r.Stops).Select(s => s.Id).ToList();
        var (visitFrom, visitTo) = ServiceDay.TodayBounds();
        var visits = new Dictionary<Guid, (DateTimeOffset At, bool Skipped)>();
        if (stopIds.Count > 0)
        {
            var rows = await db.CoverageVisits.AsNoTracking()
                .Where(v => stopIds.Contains(v.StopId) && v.ArrivedAtUtc >= visitFrom && v.ArrivedAtUtc < visitTo)
                .ToListAsync(ct);
            foreach (var group in rows.GroupBy(v => v.StopId))
            {
                var latest = group.OrderByDescending(v => v.ArrivedAtUtc).First();
                visits[group.Key] = (latest.ArrivedAtUtc, latest.Skipped);
            }
        }

        return routes.Select(r => r.ToDto(visits)).ToList();
    }
}
