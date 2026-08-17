using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api;

internal static class CaseWorkflow
{
    public static async Task AttachNoShowAsync(RoutingDbContext db, RouteStop stop, DateTimeOffset now)
    {
        var key = SettlementNames.Key(stop.SettlementName);
        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == stop.RouteId);
        var open = await db.Cases
            .Where(c => c.SettlementKey == key && (c.Status == CaseStatuses.Open || c.Status == CaseStatuses.InProgress))
            .OrderByDescending(c => c.UpdatedAtUtc)
            .FirstOrDefaultAsync();

        if (open is null)
        {
            open = new SettlementCase
            {
                Id = Guid.NewGuid(),
                SettlementKey = key,
                SettlementName = key,
                VehicleId = route?.VehicleId,
                Status = CaseStatuses.Open,
                ReportCount = 0,
                OpenedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Cases.Add(open);
        }
        else if (open.VehicleId is null && route is not null)
        {
            open.VehicleId = route.VehicleId;
        }

        open.ReportCount += 1;
        open.UpdatedAtUtc = now;
        db.CaseEvents.Add(new CaseEvent
        {
            Id = Guid.NewGuid(),
            CaseId = open.Id,
            Kind = CaseEventKinds.Report,
            Body = "Житель: не приехала",
            StopId = stop.Id,
            StopLabel = stop.SettlementName,
            CreatedAtUtc = now
        });
    }

    public static Task<SettlementCase?> LoadAsync(RoutingDbContext db, Guid id)
        => db.Cases.AsNoTracking()
            .Include(c => c.Events.OrderByDescending(e => e.CreatedAtUtc))
            .FirstOrDefaultAsync(c => c.Id == id);
}
