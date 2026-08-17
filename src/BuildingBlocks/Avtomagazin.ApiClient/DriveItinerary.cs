namespace Avtomagazin.ApiClient;

public sealed record DriveStopRow(
    RouteStopDto Stop,
    bool IsNext,
    bool Done,
    string Time,
    string Status);

public sealed record DriveItinerary(
    IReadOnlyList<RouteStopDto> Stops,
    RouteStopDto? Next,
    string NextTitle,
    string NextMeta,
    IReadOnlyList<DriveStopRow> Rows)
{
    public static DriveItinerary FromStops(IEnumerable<RouteStopDto>? stops)
    {
        var ordered = (stops ?? []).OrderBy(s => s.Sequence).ToList();
        var next = ordered.FirstOrDefault(s => s.ArrivedAtUtc is null);

        string title;
        string meta;
        if (next is null)
        {
            title = ordered.Count == 0 ? "Нет остановок на сегодня" : "Рейс завершён";
            meta = ordered.Count == 0 ? "" : "Все остановки отмечены";
        }
        else
        {
            title = next.SettlementName;
            meta = BelarusTime.Clock(next.PlannedArrivalUtc);
        }

        var rows = ordered.Select(stop =>
        {
            var done = stop.ArrivedAtUtc is not null;
            var isNext = !done && next?.Id == stop.Id;
            var status = stop.Skipped
                ? "пропущена"
                : done
                    ? $"был {BelarusTime.Clock(stop.ArrivedAtUtc!.Value)}"
                    : isNext ? "сейчас" : "";
            return new DriveStopRow(
                stop,
                isNext,
                done,
                BelarusTime.Clock(stop.PlannedArrivalUtc),
                status);
        }).ToList();

        return new DriveItinerary(ordered, next, title, meta, rows);
    }

    public static string Subtitle(VehicleDto? van, RouteDto? route)
    {
        if (van is null)
        {
            return "Назначьте автолавку в админке";
        }

        return route is null ? van.OperatorName : $"{van.OperatorName} · {route.Name}";
    }

    public static string DelayMeta(RouteStopDto next, DateTimeOffset now)
    {
        var plan = BelarusTime.Clock(next.PlannedArrivalUtc);
        var late = now - next.PlannedArrivalUtc;
        if (late < TimeSpan.FromMinutes(1))
        {
            return plan;
        }

        var mins = Math.Max(1, (int)late.TotalMinutes);
        return mins < 60
            ? $"{plan} · опоздание {mins} мин"
            : $"{plan} · опоздание {mins / 60} ч {mins % 60} мин";
    }
}
