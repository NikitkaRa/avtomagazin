using System.Net;
using System.Security.Claims;
using System.Text;
using Avtomagazin.Contracts;
using Avtomagazin.ServiceDefaults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Avtomagazin.UnitTests;

public class SessionGuardTests
{
    [Fact]
    public async Task Accepts_active_matching_version()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new StubHandler(HttpStatusCode.OK, """{"status":"active","tokenVersion":1}"""));
        Assert.True(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Rejects_version_mismatch()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new StubHandler(HttpStatusCode.OK, """{"status":"active","tokenVersion":2}"""));
        Assert.False(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Fail_open_when_identity_is_down()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}"));
        Assert.True(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Fail_closed_in_production_when_identity_is_down()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}"), Environments.Production);
        Assert.False(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Fail_open_when_identity_throws()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new ThrowingHandler());
        Assert.True(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Fail_closed_in_production_when_identity_throws()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new ThrowingHandler(), Environments.Production);
        Assert.False(await guard.ValidateAsync(Principal(userId, 1)));
    }

    [Fact]
    public async Task Rejects_missing_user()
    {
        var userId = Guid.NewGuid();
        var guard = Guard(new StubHandler(HttpStatusCode.NotFound, ""));
        Assert.False(await guard.ValidateAsync(Principal(userId, 1)));
    }

    private static HttpSessionGuard Guard(HttpMessageHandler handler, string environment = "Development")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity/") };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Internal:Key"] = "dev-internal-key"
        }).Build();
        return new HttpSessionGuard(http, cache, config, new StubEnv(environment));
    }

    private static ClaimsPrincipal Principal(Guid userId, int tokenVersion)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(AuthClaims.TokenVersion, tokenVersion.ToString())
        ], "test"));

    private sealed class StubEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("identity down");
    }
}
