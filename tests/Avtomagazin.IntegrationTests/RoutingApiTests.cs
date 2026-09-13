using System.Net;
using System.Net.Http.Json;
using Avtomagazin.Contracts;

namespace Avtomagazin.IntegrationTests;

public class RoutingApiTests : IClassFixture<RoutingApiFactory>
{
    private readonly RoutingApiFactory _factory;
    private readonly HttpClient _anonymous;

    public RoutingApiTests(RoutingApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task Anonymous_cannot_read_routes()
    {
        var response = await _anonymous.GetAsync("/api/routes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_can_read_routes()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.GetAsync("/api/routes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Озеричино", body);
    }

    [Fact]
    public async Task Driver_cannot_arrive_for_other_van()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Driver_can_arrive_own_van()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            latitude = TestJwt.OzerichinoLat,
            longitude = TestJwt.OzerichinoLng
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_arrive_at_stop_on_another_route()
    {
        var grodnoStop = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.PostAsJsonAsync($"/api/stops/{grodnoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Coverage_visit_uses_the_same_route_guard_as_arrived()
    {
        var grodnoStop = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var blocked = await client.PostAsJsonAsync("/api/coverage/visit", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            stopId = grodnoStop
        });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/coverage/visit", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            stopId = TestJwt.OzerichinoStop,
            latitude = TestJwt.OzerichinoLat,
            longitude = TestJwt.OzerichinoLng
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_arrive_without_coordinates()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_arrive_from_far_away()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        var response = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            latitude = 53.9,
            longitude = 27.5
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_arrive_without_coordinates()
    {
        using var client = TestJwt.Client(_factory, Roles.Operator);
        var response = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/arrived", new
        {
            vehicleId = TestJwt.PukhovichiVan
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Resident_forbidden_on_presence_and_coverage()
    {
        using var client = TestJwt.Client(_factory, Roles.Resident);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/presence-reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/coverage")).StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_create_route()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        var response = await client.PostAsJsonAsync("/api/routes", new
        {
            name = "Лишний",
            vehicleId = TestJwt.GrodnoVan
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resident_cannot_list_driver_notes()
    {
        using var client = TestJwt.Client(_factory, Roles.Resident);
        var response = await client.GetAsync("/api/driver-notes");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Presence_report_requires_jwt()
    {
        var response = await _anonymous.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/reports", new
        {
            kind = "on-site",
            deviceToken = "anon"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Presence_report_is_once_per_user_per_window()
    {
        var userId = Guid.NewGuid();
        using var client = TestJwt.Client(_factory, Roles.Resident, userId: userId);
        var first = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/reports", new
        {
            kind = "on-site"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/stops/{TestJwt.OzerichinoStop}/reports", new
        {
            kind = "no-show"
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Add_stop_rejects_empty_name()
    {
        var routeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        using var client = TestJwt.Client(_factory, Roles.Operator);
        var response = await client.PostAsJsonAsync($"/api/routes/{routeId}/stops", new
        {
            sequence = 99,
            settlementName = (string?)null,
            regionCode = (string?)null,
            latitude = 53.5,
            longitude = 27.5,
            plannedArrivalUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
