namespace Avtomagazin.Contracts;

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

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        => DistanceKm(lat1, lon1, lat2, lon2) * 1000;

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}

/// <summary>Server and clients share the same arrival radius.</summary>
public static class GeoFence
{
    public const double ArrivalMeters = 150;

    public static bool IsOnSite(double lat, double lng, double stopLat, double stopLng)
        => GeoMath.DistanceMeters(lat, lng, stopLat, stopLng) <= ArrivalMeters;
}
