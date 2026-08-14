using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avtomagazin.IntegrationTests;

/// <summary>
/// End-to-end checks against the running stack (Gateway :5100).
/// </summary>
public class LiveStackApiTests
{
    private static readonly Uri Gateway = new("http://127.0.0.1:5100/");

    [Fact]
    public async Task Gateway_health_and_fleet_flow()
    {
        using var client = new HttpClient { BaseAddress = Gateway, Timeout = TimeSpan.FromSeconds(10) };

        HttpResponseMessage health;
        try
        {
            health = await client.GetAsync("/health");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Gateway not reachable on {Gateway}. Start ./scripts/start-dev.sh first. ({ex.Message})");
            return;
        }

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var login = await client.PostAsJsonAsync("/identity/api/auth/login", new
        {
            email = "operator@demo.by",
            password = "demo"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var vehicles = await client.GetAsync("/fleet/api/vehicles");
        Assert.Equal(HttpStatusCode.OK, vehicles.StatusCode);
        Assert.Contains("1234 AB-7", await vehicles.Content.ReadAsStringAsync());

        var register = await client.PostAsJsonAsync("/notifications/api/devices/register", new
        {
            deviceToken = $"live-{Guid.NewGuid():N}",
            platform = "ios",
            settlementName = "Индура"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var routes = await client.GetAsync("/routing/api/routes");
        Assert.Equal(HttpStatusCode.OK, routes.StatusCode);
        var routesBody = await routes.Content.ReadAsStringAsync();
        Assert.Contains("Озеричино", routesBody);
        Assert.Contains("Пуховичи", routesBody);
        Assert.Contains("Правдинский", routesBody);

        using var routesDoc = JsonDocument.Parse(routesBody);
        Guid? ozerichinoId = null;
        foreach (var route in routesDoc.RootElement.EnumerateArray())
        {
            foreach (var stop in route.GetProperty("stops").EnumerateArray())
            {
                if (stop.GetProperty("settlementName").GetString() == "Озеричино")
                {
                    ozerichinoId = stop.GetProperty("id").GetGuid();
                }
            }
        }

        Assert.True(ozerichinoId.HasValue);
        var stopId = ozerichinoId.Value;
        var favorite = await client.PostAsJsonAsync("/notifications/api/favorites", new
        {
            deviceToken = $"live-{Guid.NewGuid():N}",
            platform = "web",
            stopId,
            settlementName = "Озеричино"
        });
        Assert.Equal(HttpStatusCode.OK, favorite.StatusCode);

        var arrived = await client.PostAsJsonAsync($"/routing/api/stops/{stopId}/arrived", new
        {
            vehicleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        });
        Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
    }
}
