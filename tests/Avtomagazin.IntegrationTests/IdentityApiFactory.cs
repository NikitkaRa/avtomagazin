using Avtomagazin.Identity.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Avtomagazin.IntegrationTests;

public sealed class IdentityApiFactory : WebApplicationFactory<AssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
