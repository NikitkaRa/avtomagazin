using Avtomagazin.Routing.Api.Data;

namespace Avtomagazin.Routing.Api;

public static class NextStop
{
    public static RouteStop Pick(IReadOnlyList<RouteStop> stops, double latitude, double longitude)
    {
        var ordered = stops.OrderBy(s => s.Sequence).ToList();
        if (ordered.Count == 0)
        {
            throw new ArgumentException("Route has no stops", nameof(stops));
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var distance = DistanceKm(latitude, longitude, current.Latitude, current.Longitude);
            if (distance < 0.25 && i + 1 < ordered.Count)
            {
                return ordered[i + 1];
            }

            if (i + 1 < ordered.Count)
            {
                var next = ordered[i + 1];
                var nextDistance = DistanceKm(latitude, longitude, next.Latitude, next.Longitude);
                if (nextDistance + 0.4 < distance)
                {
                    continue;
                }
            }

            return current;
        }

        return ordered[^1];
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthKm = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180.0;
}
