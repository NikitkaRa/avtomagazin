namespace Avtomagazin.Contracts;

public sealed record VehicleDto(
    Guid Id,
    string PlateNumber,
    string OperatorName,
    bool IsActive,
    double? LastLatitude,
    double? LastLongitude,
    DateTimeOffset? LastSeenAtUtc,
    string? LastSource,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SellerName = null,
    string? SellerPhone = null,
    string? OperatorPhone = null,
    Guid? DriverUserId = null,
    Guid? SellerUserId = null,
    string? PhotoDataUrl = null)
{
    public bool IsLive(TimeSpan? maxAge = null)
        => LastLatitude is not null
           && LastLongitude is not null
           && GpsLive.IsFresh(LastSeenAtUtc, maxAge: maxAge);
}

public sealed record UpsertVehicleRequest(
    string PlateNumber,
    string OperatorName,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SellerName = null,
    string? SellerPhone = null,
    string? OperatorPhone = null,
    Guid? DriverUserId = null,
    Guid? SellerUserId = null,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null,
    bool? IsActive = null);

public sealed record CreateVehicleRequest(string PlateNumber, string OperatorName);

public sealed record SetActiveRequest(bool IsActive);

public sealed record UpdateVehicleContactsRequest(
    string? DriverName,
    string? DriverPhone,
    string? SellerName,
    string? SellerPhone,
    string? OperatorPhone);

public sealed record PositionIngestRequest(
    double Latitude,
    double Longitude,
    double? SpeedKmh);

public sealed record VehiclePositionDto(
    Guid Id,
    Guid VehicleId,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset RecordedAtUtc,
    string Source);
