using Avtomagazin.ApiClient;
using Avtomagazin.Contracts;

namespace Avtomagazin.Admin;

public sealed record RouteProgress(
    Guid VehicleId,
    string RouteName,
    string Plate,
    string NextStop,
    int? DelayMinutes,
    string DelayLabel,
    string ScheduleLabel,
    bool Live,
    bool HasGps,
    string? DriverNote)
{
    public bool IsLate => DelayMinutes is > 2;
    public bool HasDriverNote => !string.IsNullOrWhiteSpace(DriverNote);
}

public sealed record TripHistoryItem(DateTimeOffset At, string Kind, string Body, Guid? CaseId);

internal static class DispatchBoard
{
    public static IEnumerable<RouteProgress> ActiveRoutes(
        IEnumerable<RouteDto> routes,
        IReadOnlyList<VehicleDto> vans,
        IReadOnlyList<DriverNoteDto> notes)
    {
        var rows = new List<RouteProgress>();
        foreach (var route in routes)
        {
            var van = VanOf(vans, route.VehicleId);
            if (van is null || !van.IsActive)
            {
                continue;
            }

            var stops = (route.Stops ?? []).OrderBy(s => s.Sequence).ToList();
            if (stops.Count == 0)
            {
                continue;
            }

            if (!van.IsLive() && van.LastLatitude is null && stops.All(s => s.ArrivedAtUtc is not null))
            {
                continue;
            }

            var next = stops.FirstOrDefault(s => s.ArrivedAtUtc is null)
                       ?? stops
                           .Where(s => s.PlannedArrivalUtc >= DateTimeOffset.UtcNow.AddMinutes(-30))
                           .OrderBy(s => s.PlannedArrivalUtc)
                           .FirstOrDefault()
                       ?? stops.Last();

            var scheduleLabel = next is null
                ? ""
                : $"план {next.PlannedArrivalUtc.ToLocalTime():HH:mm}";
            var delayLabel = van.IsLive() ? "в эфире" : "нет GPS";
            var note = notes.FirstOrDefault(n => n.VehicleId == van.Id);

            rows.Add(new RouteProgress(
                van.Id,
                route.Name,
                van.PlateNumber,
                next?.SettlementName ?? "",
                null,
                delayLabel,
                scheduleLabel,
                van.IsLive(),
                van.LastLatitude is not null,
                note?.Body));
        }

        return rows
            .OrderByDescending(r => r.HasDriverNote)
            .ThenByDescending(r => r.Live)
            .ThenByDescending(r => r.HasGps)
            .ThenBy(r => r.RouteName);
    }

    public static List<TripHistoryItem> TripHistory(
        Guid vehicleId,
        IEnumerable<RouteStopDto> stops,
        DriverNoteDto? note,
        IEnumerable<CaseSummaryDto> cases)
    {
        var items = new List<TripHistoryItem>();
        if (note is not null)
        {
            items.Add(new TripHistoryItem(note.CreatedAtUtc, "Водитель", note.Body, null));
        }

        foreach (var stop in stops.Where(s => s.ArrivedAtUtc is not null))
        {
            items.Add(new TripHistoryItem(
                stop.ArrivedAtUtc!.Value,
                stop.Skipped ? "Пропущена" : "На месте",
                stop.SettlementName,
                null));
        }

        foreach (var c in cases.Where(x => x.VehicleId == vehicleId))
        {
            items.Add(new TripHistoryItem(
                c.OpenedAtUtc,
                "Жалоба",
                $"{c.SettlementName} · {People(c.ReportCount)}",
                c.Id));
        }

        return items.OrderByDescending(i => i.At).ToList();
    }

    public static VehicleDto? VanOf(IEnumerable<VehicleDto> vans, Guid? vehicleId)
        => vehicleId is Guid id ? vans.FirstOrDefault(v => v.Id == id) : null;

    public static bool OnSite(VehicleDto van, RouteStopDto stop)
    {
        if (van.LastLatitude is not double lat || van.LastLongitude is not double lng)
        {
            return false;
        }

        return GeoMath.DistanceMeters(lat, lng, stop.Latitude, stop.Longitude) <= 150;
    }

    public static string StatusLabel(string status) => status switch
    {
        "open" => "открыта",
        "in_progress" => "в процессе",
        "closed" => "закрыта",
        _ => status
    };

    public static string EventKind(CaseEventDto ev) => ev.Kind switch
    {
        "report" => "Жалоба",
        "status" => "Статус",
        "comment" => "Комментарий",
        _ => ev.Kind
    };

    public static string People(int count) => count switch
    {
        1 => "1 человек",
        >= 2 and <= 4 => $"{count} человека",
        _ => $"{count} человек"
    };

    public static string Digits(string phone)
        => new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());

    public static string Ago(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (delta.TotalSeconds < 60)
        {
            return "сейчас";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} мин";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} ч";
        }

        return when.ToLocalTime().ToString("dd.MM HH:mm");
    }
}
