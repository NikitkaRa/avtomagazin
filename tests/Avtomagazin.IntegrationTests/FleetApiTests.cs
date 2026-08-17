using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avtomagazin.Contracts;

namespace Avtomagazin.IntegrationTests;

public class FleetApiTests : IClassFixture<FleetApiFactory>
{
    private readonly FleetApiFactory _factory;
    private readonly HttpClient _anonymous;

    public FleetApiTests(FleetApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Anonymous_cannot_list_vehicles()
    {
        var response = await _anonymous.GetAsync("/api/vehicles");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_can_list_vehicles()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.GetAsync("/api/vehicles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resident_cannot_ingest_position()
    {
        using var client = TestJwt.Client(_factory, Roles.Resident);
        var response = await client.PostAsJsonAsync($"/api/vehicles/{TestJwt.GrodnoVan}/positions", new
        {
            latitude = 53.6,
            longitude = 23.8
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_write_other_van()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.PostAsJsonAsync($"/api/vehicles/{TestJwt.PukhovichiVan}/positions", new
        {
            latitude = 53.5,
            longitude = 28.2
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Driver_can_write_own_van()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.PostAsJsonAsync($"/api/vehicles/{TestJwt.PukhovichiVan}/positions", new
        {
            latitude = 53.509,
            longitude = 28.247,
            source = GpsSources.DriverApp
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var position = await client.GetFromJsonAsync<JsonElement>($"/api/vehicles/{TestJwt.PukhovichiVan}/position");
        Assert.Equal(53.509, position.GetProperty("latitude").GetDouble(), 3);
        Assert.Equal(28.247, position.GetProperty("longitude").GetDouble(), 3);
        Assert.Equal("driver-app", position.GetProperty("source").GetString());

        var vehicle = await client.GetFromJsonAsync<JsonElement>($"/api/vehicles/{TestJwt.PukhovichiVan}");
        Assert.Equal(53.509, vehicle.GetProperty("lastLatitude").GetDouble(), 3);
        Assert.Equal("driver-app", vehicle.GetProperty("lastSource").GetString());
    }

    [Fact]
    public async Task Driver_cannot_create_vehicle()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.PostAsJsonAsync("/api/vehicles", new
        {
            plateNumber = $"TST-{Guid.NewGuid():N}"[..12],
            operatorName = "Тест"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_create_vehicle()
    {
        using var client = TestJwt.Client(_factory, Roles.Operator);
        var plate = $"{Random.Shared.Next(1000, 9999)} QQ-{Random.Shared.Next(0, 9)}";
        var response = await client.PostAsJsonAsync("/api/vehicles", new
        {
            plateNumber = plate,
            operatorName = "Райпо"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(plate, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Райпо", body);
    }

    [Fact]
    public async Task Create_with_driver_user_snapshots_name_and_contacts_patch_ignored()
    {
        var driverUserId = Guid.NewGuid();
        var plate = $"{Random.Shared.Next(1000, 9999)} WW-{Random.Shared.Next(0, 9)}";
        using var client = TestJwt.Client(_factory, Roles.Operator);

        var create = await client.PostAsJsonAsync("/api/vehicles", new
        {
            plateNumber = plate,
            operatorName = "Райпо",
            driverUserId = driverUserId,
            driverName = "Иван Водитель",
            driverPhone = "+375291111111"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(driverUserId, created.RootElement.GetProperty("driverUserId").GetGuid());
        Assert.Equal("Иван Водитель", created.RootElement.GetProperty("driverName").GetString());

        var patch = await client.PatchAsJsonAsync($"/api/vehicles/{id}/contacts", new
        {
            driverName = "Хакер",
            driverPhone = "+375290000000",
            sellerName = "Продавец",
            sellerPhone = "+375292222222",
            operatorPhone = "+375293333333"
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var patched = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        Assert.Equal("Иван Водитель", patched.RootElement.GetProperty("driverName").GetString());
        Assert.Equal("+375291111111", patched.RootElement.GetProperty("driverPhone").GetString());
        Assert.Equal("Продавец", patched.RootElement.GetProperty("sellerName").GetString());
        Assert.Equal("+375293333333", patched.RootElement.GetProperty("operatorPhone").GetString());
    }

    [Fact]
    public async Task Update_clearing_driver_user_clears_snapshot()
    {
        var plate = $"{Random.Shared.Next(1000, 9999)} VV-{Random.Shared.Next(0, 9)}";
        using var client = TestJwt.Client(_factory, Roles.Operator);
        var create = await client.PostAsJsonAsync("/api/vehicles", new
        {
            plateNumber = plate,
            operatorName = "Райпо",
            driverUserId = Guid.NewGuid(),
            driverName = "Был"
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync($"/api/vehicles/{id}", new
        {
            plateNumber = plate,
            operatorName = "Райпо",
            driverUserId = (Guid?)null,
            driverName = "Не должно остаться"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, updated.RootElement.GetProperty("driverUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.RootElement.GetProperty("driverName").ValueKind);
    }
}
