using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Consumers;

public sealed class VehiclePositionUpdatedConsumer(
    RoutingDbContext db,
    IPublishEndpoint bus,
    ILogger<VehiclePositionUpdatedConsumer> logger) : IConsumer<VehiclePositionUpdated>
{
    public async Task Consume(ConsumeContext<VehiclePositionUpdated> context)
    {
        var msg = context.Message;
        var route = await db.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.VehicleId == msg.VehicleId, context.CancellationToken);

        if (route is null || route.Stops.Count == 0)
        {
            return;
        }

        var nextStop = NextStop.Pick(route.Stops, msg.Latitude, msg.Longitude);

        var distanceKm = NextStop.DistanceKm(msg.Latitude, msg.Longitude, nextStop.Latitude, nextStop.Longitude);
        var speed = Math.Max(msg.SpeedKmh ?? 30, 10);
        var minutes = (int)Math.Ceiling(distanceKm / speed * 60);
        var eta = msg.RecordedAtUtc.AddMinutes(minutes);

        var stale = db.EtaSnapshots.Where(e => e.VehicleId == msg.VehicleId);
        db.EtaSnapshots.RemoveRange(stale);

        var snapshot = new EtaSnapshot
        {
            Id = Guid.NewGuid(),
            VehicleId = msg.VehicleId,
            RouteId = route.Id,
            StopId = nextStop.Id,
            SettlementName = nextStop.SettlementName,
            EstimatedArrivalUtc = eta,
            MinutesUntilArrival = minutes,
            CalculatedAtUtc = DateTimeOffset.UtcNow
        };

        db.EtaSnapshots.Add(snapshot);
        await db.SaveChangesAsync(context.CancellationToken);

        await bus.Publish(new StopArrivalEstimated(
            msg.VehicleId,
            route.Id,
            nextStop.Id,
            nextStop.SettlementName,
            eta,
            minutes), context.CancellationToken);

        logger.LogInformation(
            "ETA for {Settlement}: {Minutes} min (vehicle {Plate})",
            nextStop.SettlementName,
            minutes,
            msg.PlateNumber);
    }
}
