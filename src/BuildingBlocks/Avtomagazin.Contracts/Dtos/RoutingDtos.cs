namespace Avtomagazin.Contracts;

public sealed record RouteDto(Guid Id, string Name, Guid VehicleId, List<RouteStopDto>? Stops)
{
    public static RouteDto? ForVehicle(IEnumerable<RouteDto> routes, Guid vehicleId)
        => routes
            .Where(r => r.VehicleId == vehicleId)
            .Select(r => (
                Route: r,
                Next: r.Stops?
                    .Where(s => s.ArrivedAtUtc is null)
                    .OrderBy(s => s.Sequence)
                    .FirstOrDefault()))
            .OrderBy(x => x.Next is null ? 1 : 0)
            .ThenBy(x => x.Next?.PlannedArrivalUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Route.Name)
            .Select(x => x.Route)
            .FirstOrDefault();
}

public sealed record RouteStopDto(
    Guid Id,
    Guid RouteId,
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc,
    string? PhotoDataUrl = null,
    DateTimeOffset? ArrivedAtUtc = null,
    bool Skipped = false);

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

public sealed record DriverNoteDto(
    Guid Id,
    Guid VehicleId,
    string Body,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAtUtc);

public sealed record PostDriverNoteRequest(
    Guid VehicleId,
    string Body,
    double? Latitude,
    double? Longitude);

public sealed record CoverageDto(
    Guid Id,
    Guid VehicleId,
    Guid StopId,
    string SettlementName,
    string RegionCode,
    DateTimeOffset ArrivedAtUtc,
    bool WithinScheduledWindow,
    bool Skipped = false);

public sealed record CoverageVisitRequest(
    Guid VehicleId,
    Guid StopId,
    DateTimeOffset? ArrivedAtUtc,
    double? Latitude = null,
    double? Longitude = null);

public sealed record DriverArrivedRequest(
    Guid VehicleId,
    bool Skipped = false,
    double? Latitude = null,
    double? Longitude = null);

public sealed record PresenceReportRequest(string Kind, string? DeviceToken);

public sealed record PresenceReportDto(
    Guid Id,
    Guid StopId,
    string SettlementName,
    string Kind,
    string? DeviceToken,
    DateTimeOffset ReportedAtUtc);

public sealed record CaseSummaryDto(
    Guid Id,
    string SettlementKey,
    string SettlementName,
    Guid? VehicleId,
    string Status,
    int ReportCount,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record CaseDetailDto(
    Guid Id,
    string SettlementKey,
    string SettlementName,
    Guid? VehicleId,
    string Status,
    int ReportCount,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    List<CaseEventDto> Events);

public sealed record CaseEventDto(
    Guid Id,
    Guid CaseId,
    string Kind,
    string Body,
    string? AuthorName,
    string? AuthorEmail,
    Guid? StopId,
    string? StopLabel,
    string? FromStatus,
    string? ToStatus,
    DateTimeOffset CreatedAtUtc);

public sealed record CaseCommentRequest(string Body);

public sealed record CaseStatusRequest(string Status, string? Comment);
