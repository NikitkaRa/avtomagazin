using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avtomagazin.IntegrationTests;

public class IdentityApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly HttpClient _client;

    public IdentityApiTests(IdentityApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_with_demo_user_returns_jwt()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "resident@demo.by",
            password = "demo"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("accessToken").GetString()));
        Assert.Equal("resident", doc.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_seller_is_driver_for_pukhovichi_van()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "seller@demo.by",
            password = "demo"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("driver", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            doc.RootElement.GetProperty("vehicleId").GetGuid());
    }

    [Fact]
    public async Task Login_with_bad_password_returns_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "resident@demo.by",
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
