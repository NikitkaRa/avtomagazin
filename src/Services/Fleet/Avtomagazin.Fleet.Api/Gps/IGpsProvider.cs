namespace Avtomagazin.Fleet.Api.Gps;

/// <summary>
/// Anti-corruption port for telematics vendors (Wialon, Traccar, BelGPS, ...).
/// Swap MockGpsProvider for a real adapter without touching domain endpoints.
/// </summary>
public interface IGpsProvider
{
    Task<IReadOnlyList<GpsFix>> PollAsync(CancellationToken cancellationToken);
}

public sealed record GpsFix(
    Guid VehicleId,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset RecordedAtUtc);
