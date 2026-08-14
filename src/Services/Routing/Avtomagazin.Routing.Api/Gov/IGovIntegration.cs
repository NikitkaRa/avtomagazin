using Avtomagazin.Contracts.Events;

namespace Avtomagazin.Routing.Api.Gov;

/// <summary>
/// Port for future "Умный город (регион)" / Minsvyaz integrations.
/// </summary>
public interface IGovIntegration
{
    Task PublishCoverageAsync(CoverageVisitRecorded visit, CancellationToken cancellationToken = default);
}

public sealed class StubGovIntegration(ILogger<StubGovIntegration> logger) : IGovIntegration
{
    public Task PublishCoverageAsync(CoverageVisitRecorded visit, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "GOV STUB → coverage visit {Settlement} ({Region}) at {At}, on-time={OnTime}",
            visit.SettlementName,
            visit.RegionCode,
            visit.ArrivedAtUtc,
            visit.WithinScheduledWindow);

        return Task.CompletedTask;
    }
}
