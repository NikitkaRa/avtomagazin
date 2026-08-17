using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Data;
using Avtomagazin.Routing.Api.Gov;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api;

internal static class CoverageVisits
{
    public static CoverageVisit Create(
        Guid vehicleId,
        RouteStop stop,
        DateTimeOffset arrivedAt,
        bool skipped = false)
    {
        var onTime = !skipped && ScheduleWindow.Contains(stop.PlannedArrivalUtc, arrivedAt);
        return new CoverageVisit
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            StopId = stop.Id,
            SettlementName = stop.SettlementName,
            RegionCode = stop.RegionCode,
            ArrivedAtUtc = arrivedAt,
            WithinScheduledWindow = onTime,
            Skipped = skipped
        };
    }

    public static async Task<CoverageVisit> SaveAsync(RoutingDbContext db, CoverageVisit visit, CancellationToken ct = default)
    {
        db.CoverageVisits.Add(visit);
        await db.SaveChangesAsync(ct);
        return visit;
    }

    public static CoverageVisitRecorded ToEvent(CoverageVisit visit)
        => new(
            visit.VehicleId,
            visit.StopId,
            visit.SettlementName,
            visit.RegionCode,
            visit.ArrivedAtUtc,
            visit.WithinScheduledWindow);

    public static async Task PublishCoverageAsync(
        CoverageVisit visit,
        IPublishEndpoint bus,
        IGovIntegration gov,
        CancellationToken ct = default)
    {
        var evt = ToEvent(visit);
        await bus.Publish(evt, ct);
        await gov.PublishCoverageAsync(evt);
    }

    public static async Task PublishDriverArrivalAsync(
        Guid vehicleId,
        RouteStop stop,
        DateTimeOffset arrivedAt,
        IPublishEndpoint bus,
        CancellationToken ct = default)
        => await bus.Publish(new DriverArrivedAtStop(
            vehicleId,
            stop.RouteId,
            stop.Id,
            stop.SettlementName,
            arrivedAt), ct);

    public static async Task<RouteStop?> FindStopAsync(RoutingDbContext db, Guid stopId, CancellationToken ct = default)
        => await db.Stops.FirstOrDefaultAsync(s => s.Id == stopId, ct);
}
