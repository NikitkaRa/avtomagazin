using System.Net;
using System.Net.Http.Json;
using Avtomagazin.Contracts;

namespace Avtomagazin.IntegrationTests;

public class DriverNoteApiTests : IClassFixture<RoutingApiFactory>
{
    private readonly RoutingApiFactory _factory;

    public DriverNoteApiTests(RoutingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Driver_can_post_and_clear_note_with_coords()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.GrodnoVan);
        await client.DeleteAsync($"/api/driver-notes/{TestJwt.GrodnoVan}");
        var post = await client.PostAsJsonAsync("/api/driver-notes", new
        {
            vehicleId = TestJwt.GrodnoVan,
            body = "Пробил колесо",
            latitude = 53.67,
            longitude = 23.81
        });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var list = await client.GetFromJsonAsync<List<Note>>("/api/driver-notes");
        Assert.NotNull(list);
        Assert.Contains(list, n => n.Body == "Пробил колесо" && n.VehicleId == TestJwt.GrodnoVan);

        var clear = await client.DeleteAsync($"/api/driver-notes/{TestJwt.GrodnoVan}");
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
    }

    [Fact]
    public async Task Driver_cannot_replace_note_until_cleared()
    {
        using var client = TestJwt.Client(_factory, Roles.Driver, TestJwt.PukhovichiVan);
        await client.DeleteAsync($"/api/driver-notes/{TestJwt.PukhovichiVan}");

        var first = await client.PostAsJsonAsync("/api/driver-notes", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            body = "Завяз",
            latitude = 53.57,
            longitude = 27.74
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/driver-notes", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            body = "Пробил колесо",
            latitude = 53.57,
            longitude = 27.74
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var list = await client.GetFromJsonAsync<List<Note>>("/api/driver-notes");
        Assert.NotNull(list);
        Assert.Contains(list, n => n.Body == "Завяз" && n.VehicleId == TestJwt.PukhovichiVan);
        Assert.DoesNotContain(list, n => n.Body == "Пробил колесо" && n.VehicleId == TestJwt.PukhovichiVan);

        var clear = await client.DeleteAsync($"/api/driver-notes/{TestJwt.PukhovichiVan}");
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);

        var third = await client.PostAsJsonAsync("/api/driver-notes", new
        {
            vehicleId = TestJwt.PukhovichiVan,
            body = "Пробил колесо",
            latitude = 53.57,
            longitude = 27.74
        });
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        await client.DeleteAsync($"/api/driver-notes/{TestJwt.PukhovichiVan}");
    }

    private sealed record Note(Guid Id, Guid VehicleId, string Body, double Latitude, double Longitude);
}
