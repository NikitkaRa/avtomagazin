using Avtomagazin.Contracts.Events;
using Avtomagazin.Routing.Api.Gov;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avtomagazin.UnitTests;

public class StubGovIntegrationTests
{
    [Fact]
    public async Task PublishCoverageAsync_completes_without_throwing()
    {
        var gov = new StubGovIntegration(NullLogger<StubGovIntegration>.Instance);
        var visit = new CoverageVisitRecorded(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Индура",
            "BY-HR",
            DateTimeOffset.UtcNow,
            true);

        var exception = await Record.ExceptionAsync(() => gov.PublishCoverageAsync(visit));
        Assert.Null(exception);
    }
}
