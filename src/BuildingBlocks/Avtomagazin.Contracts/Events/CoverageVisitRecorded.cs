namespace Avtomagazin.Contracts.Events;

/// <summary>
/// Domain event for government social-standard coverage reporting.
/// Gov adapter can forward this to "Умный город" later.
/// </summary>
public sealed record CoverageVisitRecorded(
    Guid VehicleId,
    Guid StopId,
    string SettlementName,
    string RegionCode,
    DateTimeOffset ArrivedAtUtc,
    bool WithinScheduledWindow);
