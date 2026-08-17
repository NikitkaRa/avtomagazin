namespace Avtomagazin.Routing.Api;

public sealed record CoverageVisitRequest(
    Guid VehicleId,
    Guid StopId,
    DateTimeOffset? ArrivedAtUtc);

public sealed record CreateRouteRequest(string Name, Guid VehicleId);

public sealed record UpdateRouteRequest(string? Name, Guid? VehicleId);

public sealed record ReplaceStopsRequest(List<ReplaceStopItem>? Stops);

public sealed record ReplaceStopItem(
    Guid? Id,
    int Sequence,
    string SettlementName,
    string? RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset? PlannedArrivalUtc,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null);

public sealed record CreateStopRequest(
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc);

public sealed record ChangeScheduleRequest(
    Guid RouteId,
    Guid StopId,
    DateTimeOffset NewArrivalUtc,
    string Reason);

public sealed record DriverArrivedRequest(Guid VehicleId, bool Skipped = false);

public sealed record PostDriverNoteRequest(
    Guid VehicleId,
    string Body,
    double? Latitude,
    double? Longitude);

public sealed record PresenceReportRequest(string Kind, string? DeviceToken);

public sealed record CaseCommentRequest(string Body);

public sealed record CaseStatusRequest(string Status, string? Comment);
