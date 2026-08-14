namespace Avtomagazin.Contracts.Events;

public sealed record DriverArrivedAtStop(
    Guid VehicleId,
    Guid RouteId,
    Guid StopId,
    string SettlementName,
    DateTimeOffset ArrivedAtUtc);
