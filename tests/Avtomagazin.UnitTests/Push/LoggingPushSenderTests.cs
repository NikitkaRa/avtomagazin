using Avtomagazin.Notifications.Api.Push;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avtomagazin.UnitTests;

public class LoggingPushSenderTests
{
    [Fact]
    public async Task SendAsync_accepts_payload()
    {
        var sender = new LoggingPushSender(NullLogger<LoggingPushSender>.Instance);

        var exception = await Record.ExceptionAsync(
            () => sender.SendAsync("token-12345678", "Title", "Body"));

        Assert.Null(exception);
    }
}
