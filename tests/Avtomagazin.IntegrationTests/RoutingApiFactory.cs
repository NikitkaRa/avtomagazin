using Avtomagazin.Routing.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Avtomagazin.IntegrationTests;

public sealed class RoutingApiFactory : WebApplicationFactory<AssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Testing");
}
