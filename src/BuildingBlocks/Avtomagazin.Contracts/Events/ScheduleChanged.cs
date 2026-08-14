namespace Avtomagazin.Contracts.Events;

public sealed record ScheduleChanged(
    Guid RouteId,
    Guid StopId,
    string SettlementName,
    DateTimeOffset PlannedArrivalUtc,
    DateTimeOffset? NewArrivalUtc,
    string Reason);
