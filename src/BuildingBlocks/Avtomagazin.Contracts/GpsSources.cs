namespace Avtomagazin.Contracts;

public static class GpsSources
{
    public const string DriverApp = "driver-app";
    public const string Adapter = "gps-adapter";
    public const string Manual = "manual";
    public const string Seed = "seed";

    /// <summary>HTTP ingest never accepts adapter/seed timestamps from the client.</summary>
    public static string ForHttpIngest(string? role)
        => Roles.IsVanCrew(role ?? "") ? DriverApp : Manual;
}

public static class GpsLive
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    public static bool IsFresh(DateTimeOffset? lastSeenAtUtc, DateTimeOffset? now = null, TimeSpan? maxAge = null)
    {
        if (lastSeenAtUtc is not DateTimeOffset at)
        {
            return false;
        }

        return (now ?? DateTimeOffset.UtcNow) - at <= (maxAge ?? Window);
    }
}
