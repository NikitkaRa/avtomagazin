using Avtomagazin.Notifications.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Avtomagazin.IntegrationTests;

public sealed class NotificationsApiFactory : WebApplicationFactory<AssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Testing");
}
