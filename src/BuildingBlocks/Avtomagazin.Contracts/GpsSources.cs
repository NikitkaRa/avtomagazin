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
