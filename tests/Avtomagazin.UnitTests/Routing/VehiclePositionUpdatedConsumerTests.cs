using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.UnitTests.Routing;

public class VehiclePositionUpdatedConsumerTests
{
    [Fact]
    public async Task Consume_skips_eta_until_road_routing_is_wired()
    {
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<VehiclePositionUpdatedConsumer>())
            .BuildServiceProvider(true);

        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new VehiclePositionUpdated(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "1234 AB-7",
            53.67,
            23.81,
            40,
            DateTimeOffset.UtcNow));

        Assert.True(await harness.Consumed.Any<VehiclePositionUpdated>());
        Assert.False(await harness.Published.Any<StopArrivalEstimated>());

        await harness.Stop();
    }
}
