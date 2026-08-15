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
