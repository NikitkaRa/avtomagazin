using Avtomagazin.Contracts.Events;
using MassTransit;

namespace Avtomagazin.Routing.Api.Consumers;

/// <summary>
/// GPS → ETA pipeline. Straight-line Haversine ETA was removed as misleading;
/// wire Yandex (or similar) road routing here later.
/// </summary>
public sealed class VehiclePositionUpdatedConsumer(
    ILogger<VehiclePositionUpdatedConsumer> logger) : IConsumer<VehiclePositionUpdated>
{
    public Task Consume(ConsumeContext<VehiclePositionUpdated> context)
    {
        logger.LogDebug(
            "Skip ETA for {Plate}: road routing not configured",
            context.Message.PlateNumber);
        return Task.CompletedTask;
    }
}
