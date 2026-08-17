namespace Avtomagazin.Fleet.Api;

public sealed record PositionIngestRequest(
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset? RecordedAtUtc,
    string? Source);

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

public sealed record SetActiveRequest(bool IsActive);

public sealed record UpdateVehicleContactsRequest(
    string? DriverName,
    string? DriverPhone,
    string? SellerName,
    string? SellerPhone,
    string? OperatorPhone);
