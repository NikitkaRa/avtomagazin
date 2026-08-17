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
        => Avtomagazin.Contracts.GeoMath.DistanceKm(lat1, lon1, lat2, lon2);
}
