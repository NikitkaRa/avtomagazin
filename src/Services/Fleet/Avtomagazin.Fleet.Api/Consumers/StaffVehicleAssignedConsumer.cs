using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Consumers;

public sealed class StaffVehicleAssignedConsumer(
    FleetDbContext db,
    ILogger<StaffVehicleAssignedConsumer> logger) : IConsumer<StaffVehicleAssigned>
{
    public async Task Consume(ConsumeContext<StaffVehicleAssigned> context)
    {
        var msg = context.Message;
        var vehicles = await db.Vehicles
            .Where(v => v.DriverUserId == msg.UserId || v.SellerUserId == msg.UserId)
            .ToListAsync(context.CancellationToken);

        foreach (var vehicle in vehicles)
        {
            VehicleCrew.ClearUser(vehicle, msg.UserId);
        }

        if (msg.VehicleId is Guid vanId && Roles.IsVanCrew(msg.Role))
        {
            var van = vehicles.FirstOrDefault(v => v.Id == vanId)
                      ?? await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vanId, context.CancellationToken);
            if (van is null)
            {
                logger.LogWarning("StaffVehicleAssigned for unknown van {VehicleId}", vanId);
            }
            else
            {
                VehicleCrew.Assign(van, msg.Role, msg.UserId, msg.DisplayName, msg.Phone);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "Synced van assignment for user {UserId} role {Role} vehicle {VehicleId}",
            msg.UserId,
            msg.Role,
            msg.VehicleId);
    }
}
