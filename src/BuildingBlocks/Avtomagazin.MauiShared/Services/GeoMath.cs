using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public static class GeoMath
{
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earth = 6371;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earth * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static RouteStopDto? Nearest(IEnumerable<RouteStopDto> stops, double lat, double lng)
        => stops.OrderBy(s => DistanceKm(lat, lng, s.Latitude, s.Longitude)).FirstOrDefault();

    public static bool InReportWindow(DateTimeOffset plannedUtc, DateTimeOffset now)
    {
        if (Inside(plannedUtc, now))
        {
            return true;
        }

        var today = new DateTimeOffset(now.Year, now.Month, now.Day, plannedUtc.Hour, plannedUtc.Minute, 0, TimeSpan.Zero);
        return Inside(today, now);
    }

    private static bool Inside(DateTimeOffset plannedUtc, DateTimeOffset now)
        => now >= plannedUtc.AddMinutes(-15) && now <= plannedUtc.AddHours(1);

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
