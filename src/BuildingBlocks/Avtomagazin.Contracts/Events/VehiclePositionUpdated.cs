namespace Avtomagazin.Contracts.Events;

/// <summary>
/// Published by Fleet when a vehicle GPS point is ingested.
/// </summary>
public sealed record VehiclePositionUpdated(
    Guid VehicleId,
    string PlateNumber,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset RecordedAtUtc);
