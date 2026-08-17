using Avtomagazin.ApiClient;
using Avtomagazin.Contracts;

namespace Avtomagazin.MauiShared;

public static class GeoMath
{
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
        => Avtomagazin.Contracts.GeoMath.DistanceKm(lat1, lon1, lat2, lon2);

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        => Avtomagazin.Contracts.GeoMath.DistanceMeters(lat1, lon1, lat2, lon2);

    public static bool InReportWindow(DateTimeOffset plannedUtc, DateTimeOffset now)
        => ScheduleWindow.Contains(plannedUtc, now);

    public static RouteStopDto? Nearest(IEnumerable<RouteStopDto> stops, double lat, double lng)
        => stops.OrderBy(s => DistanceKm(lat, lng, s.Latitude, s.Longitude)).FirstOrDefault();
}
