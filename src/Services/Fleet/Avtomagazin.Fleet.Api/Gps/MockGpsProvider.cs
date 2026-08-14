namespace Avtomagazin.Fleet.Api.Gps;

/// <summary>
/// Simulates two vans: Grodno oblast and a loop around Озеричино.
/// </summary>
public sealed class MockGpsProvider : IGpsProvider
{
    private static readonly Guid VehicleA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VehicleB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private double _phase;

    public Task<IReadOnlyList<GpsFix>> PollAsync(CancellationToken cancellationToken)
    {
        _phase += 0.15;
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<GpsFix> fixes =
        [
            new GpsFix(
                VehicleA,
                53.6694 + Math.Sin(_phase) * 0.02,
                23.8131 + Math.Cos(_phase) * 0.02,
                38 + Math.Sin(_phase) * 5,
                now),
            new GpsFix(
                VehicleB,
                53.5774 + Math.Cos(_phase) * 0.012,
                27.7472 + Math.Sin(_phase) * 0.012,
                28 + Math.Cos(_phase) * 4,
                now)
        ];

        return Task.FromResult(fixes);
    }
}
