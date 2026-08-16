using Avtomagazin.Contracts.Events;
using Avtomagazin.Fleet.Api.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Fleet.Api.Consumers;

public sealed class StaffContactChangedConsumer(
    FleetDbContext db,
    ILogger<StaffContactChangedConsumer> logger) : IConsumer<StaffContactChanged>
{
    public async Task Consume(ConsumeContext<StaffContactChanged> context)
    {
        var msg = context.Message;
        var name = string.IsNullOrWhiteSpace(msg.DisplayName) ? null : msg.DisplayName.Trim();
        var phone = string.IsNullOrWhiteSpace(msg.Phone) ? null : msg.Phone.Trim();

        var vehicles = await db.Vehicles
            .Where(v => v.DriverUserId == msg.UserId || v.SellerUserId == msg.UserId)
            .ToListAsync(context.CancellationToken);

        if (vehicles.Count == 0)
        {
            return;
        }

        foreach (var vehicle in vehicles)
        {
            if (vehicle.DriverUserId == msg.UserId)
            {
                vehicle.DriverName = name;
                vehicle.DriverPhone = phone;
            }

            if (vehicle.SellerUserId == msg.UserId)
            {
                vehicle.SellerName = name;
                vehicle.SellerPhone = phone;
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "Synced contact snapshot for user {UserId} on {Count} vehicle(s)",
            msg.UserId,
            vehicles.Count);
    }
}
