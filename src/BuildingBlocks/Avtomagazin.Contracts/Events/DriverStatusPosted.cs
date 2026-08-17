namespace Avtomagazin.Contracts.Events;

public sealed record DriverStatusPosted(
    Guid VehicleId,
    string Body,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAtUtc);
