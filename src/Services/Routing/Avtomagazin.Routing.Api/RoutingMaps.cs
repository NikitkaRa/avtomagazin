using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;

namespace Avtomagazin.Routing.Api;

internal static class RoutingMaps
{
    public static RouteDto ToDto(
        this TradeRoute route,
        IReadOnlyDictionary<Guid, (DateTimeOffset At, bool Skipped)>? visits = null)
        => new(
            route.Id,
            route.Name,
            route.VehicleId,
            route.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => s.ToDto(visits))
                .ToList());

    public static RouteStopDto ToDto(
        this RouteStop stop,
        IReadOnlyDictionary<Guid, (DateTimeOffset At, bool Skipped)>? visits = null)
    {
        DateTimeOffset? arrived = null;
        var skipped = false;
        if (visits is not null && visits.TryGetValue(stop.Id, out var visit))
        {
            arrived = visit.At;
            skipped = visit.Skipped;
        }

        return new RouteStopDto(
            stop.Id,
            stop.RouteId,
            stop.Sequence,
            stop.SettlementName,
            stop.RegionCode,
            stop.Latitude,
            stop.Longitude,
            stop.PlannedArrivalUtc,
            stop.PhotoDataUrl,
            arrived,
            skipped);
    }

    public static DriverNoteDto ToDto(this DriverStatusNote note) => new(
        note.Id,
        note.VehicleId,
        note.Body,
        note.Latitude,
        note.Longitude,
        note.CreatedAtUtc);

    public static CoverageDto ToDto(this CoverageVisit visit) => new(
        visit.Id,
        visit.VehicleId,
        visit.StopId,
        visit.SettlementName,
        visit.RegionCode,
        visit.ArrivedAtUtc,
        visit.WithinScheduledWindow,
        visit.Skipped);

    public static PresenceReportDto ToDto(this StopPresenceReport report) => new(
        report.Id,
        report.StopId,
        report.SettlementName,
        report.Kind,
        report.DeviceToken,
        report.ReportedAtUtc);

    public static CaseSummaryDto ToSummary(this SettlementCase item) => new(
        item.Id,
        item.SettlementKey,
        item.SettlementName,
        item.VehicleId,
        item.Status,
        item.ReportCount,
        item.OpenedAtUtc,
        item.UpdatedAtUtc,
        item.ClosedAtUtc);

    public static CaseDetailDto ToDetail(this SettlementCase item) => new(
        item.Id,
        item.SettlementKey,
        item.SettlementName,
        item.VehicleId,
        item.Status,
        item.ReportCount,
        item.OpenedAtUtc,
        item.UpdatedAtUtc,
        item.ClosedAtUtc,
        item.Events.Select(ToDto).ToList());

    public static CaseEventDto ToDto(this CaseEvent item) => new(
        item.Id,
        item.CaseId,
        item.Kind,
        item.Body,
        item.AuthorName,
        item.AuthorEmail,
        item.StopId,
        item.StopLabel,
        item.FromStatus,
        item.ToStatus,
        item.CreatedAtUtc);
}
