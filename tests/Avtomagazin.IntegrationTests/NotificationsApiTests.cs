using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avtomagazin.Contracts;

namespace Avtomagazin.IntegrationTests;

public class NotificationsApiTests : IClassFixture<NotificationsApiFactory>
{
    private readonly NotificationsApiFactory _factory;
    private readonly HttpClient _anonymous;

    public NotificationsApiTests(NotificationsApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Resident_cannot_read_notification_logs()
    {
        using var client = TestJwt.Client(_factory, Roles.Resident);
        var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_can_read_notification_logs()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.GetAsync("/api/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Device_register_requires_jwt()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/devices/register", new
        {
            userId = Guid.NewGuid(),
            deviceToken = $"spoof-{Guid.NewGuid():N}",
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Device_token_cannot_be_stolen_and_short_tokens_rejected()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var token = $"dev-{Guid.NewGuid():N}";
        using var a = TestJwt.Client(_factory, Roles.Resident, userId: owner);
        using var b = TestJwt.Client(_factory, Roles.Resident, userId: other);

        var shortToken = await a.PostAsJsonAsync("/api/devices/register", new
        {
            deviceToken = "short",
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.BadRequest, shortToken.StatusCode);

        var ok = await a.PostAsJsonAsync("/api/devices/register", new
        {
            deviceToken = token,
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var stolen = await b.PostAsJsonAsync("/api/devices/register", new
        {
            deviceToken = token,
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);

        var gone = await a.DeleteAsync($"/api/devices?deviceToken={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);

        var reused = await b.PostAsJsonAsync("/api/devices/register", new
        {
            deviceToken = token,
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
    }

    [Fact]
    public async Task Device_register_binds_jwt_user_not_body()
    {
        var jwtUser = Guid.NewGuid();
        var spoof = Guid.NewGuid();
        using var client = TestJwt.Client(_factory, Roles.Resident, userId: jwtUser);
        var response = await client.PostAsJsonAsync("/api/devices/register", new
        {
            userId = spoof,
            deviceToken = $"dev-{Guid.NewGuid():N}",
            platform = "android",
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(jwtUser, doc.RootElement.GetProperty("userId").GetGuid());
    }
}
