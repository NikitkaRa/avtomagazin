namespace Avtomagazin.Notifications.Api.Push;

/// <summary>
/// Port for FCM / APNs. Replace LoggingPushSender with real provider later.
/// </summary>
public interface IPushSender
{
    Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default);
}

public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("PUSH → {TokenPrefix}… | {Title}: {Body}",
            deviceToken.Length <= 8 ? deviceToken : deviceToken[..8],
            title,
            body);
        return Task.CompletedTask;
    }
}
