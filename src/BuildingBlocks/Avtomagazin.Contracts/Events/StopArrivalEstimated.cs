namespace Avtomagazin.Contracts.Events;

public sealed record StopArrivalEstimated(
    Guid VehicleId,
    Guid RouteId,
    Guid StopId,
    string SettlementName,
    DateTimeOffset EstimatedArrivalUtc,
    int MinutesUntilArrival);
