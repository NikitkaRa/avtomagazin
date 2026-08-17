namespace Avtomagazin.Contracts;

public sealed record DeviceDto(
    Guid Id,
    Guid? UserId,
    string DeviceToken,
    string Platform,
    string SettlementName);

public sealed record FavoriteDto(Guid StopId, string SettlementName, DateTimeOffset CreatedAtUtc);

public sealed record FavoriteStatsDto(Guid StopId, string SettlementName, int SubscriberCount);

public sealed record DeviceRegistrationRequest(
    Guid? UserId,
    string DeviceToken,
    string Platform,
    string SettlementName);

public sealed record FavoriteStopRequest(
    Guid? UserId,
    string DeviceToken,
    string? Platform,
    Guid StopId,
    string SettlementName);

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Body,
    string SettlementName,
    DateTimeOffset SentAtUtc,
    int RecipientCount);

public sealed record SnapshotDto(
    DateTimeOffset? SyncedAtUtc,
    List<VehicleDto> Vehicles,
    List<RouteDto> Routes,
    List<DriverNoteDto>? DriverNotes = null);

public sealed record DispatchSnapshotDto(
    DateTimeOffset SyncedAtUtc,
    List<VehicleDto> Vehicles,
    List<CaseSummaryDto> Cases,
    List<RouteDto> Routes,
    List<DriverNoteDto> DriverNotes);
