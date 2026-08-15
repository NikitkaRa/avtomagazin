using System.Net;
using System.Net.Http.Json;
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
    public async Task Anonymous_can_list_vehicles()
    {
        var response = await _anonymous.GetAsync("/api/vehicles");
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
            source = "driver-app"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
        var plate = $"OP-{Guid.NewGuid():N}"[..10];
        var response = await client.PostAsJsonAsync("/api/vehicles", new
        {
            plateNumber = plate,
            operatorName = "Райпо"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
