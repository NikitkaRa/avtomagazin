using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddMemoryCache();

var gateway = builder.Configuration["Gateway:BaseUrl"] ?? "http://127.0.0.1:5100";
builder.Services.AddHttpClient("gateway", client =>
{
    client.BaseAddress = new Uri(gateway.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient("osm", client =>
{
    client.BaseAddress = new Uri("https://tile.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AvtomagazinResident/1.0 (offline village shop demo)");
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/config.json", (IConfiguration config) => Results.Json(new
{
    yandexMapsKey = config["Maps:YandexApiKey"] ?? ""
}));

app.MapGet("/tiles/{z:int}/{x:int}/{y:int}.png", async (
    int z,
    int x,
    int y,
    IHttpClientFactory factory,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    if (z is < 8 or > 16)
    {
        return Results.BadRequest();
    }

    var n = 1 << z;
    if (x < 0 || y < 0 || x >= n || y >= n)
    {
        return Results.BadRequest();
    }

    var key = $"osm:{z}/{x}/{y}";
    if (cache.TryGetValue(key, out byte[]? cached) && cached is { Length: > 0 })
    {
        return Results.File(cached, "image/png");
    }

    try
    {
        var http = factory.CreateClient("osm");
        using var response = await http.GetAsync($"{z}/{x}/{y}.png", ct);
        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        cache.Set(key, bytes, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromDays(14)
        });
        return Results.File(bytes, "image/png");
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/snapshot", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var http = factory.CreateClient("gateway");

    async Task<JsonElement> GetOrEmptyArray(string path)
    {
        try
        {
            using var response = await http.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                return EmptyArray();
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return payload.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                ? payload
                : EmptyArray();
        }
        catch
        {
            return EmptyArray();
        }
    }

    var vehicles = await GetOrEmptyArray("fleet/api/vehicles");
    var routes = await GetOrEmptyArray("routing/api/routes");
    var eta = await GetOrEmptyArray("routing/api/eta");

    var hasData = vehicles.ValueKind == JsonValueKind.Array && vehicles.GetArrayLength() > 0
                  || routes.ValueKind == JsonValueKind.Array && routes.GetArrayLength() > 0;

    if (!hasData)
    {
        return Results.Json(new { online = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        syncedAtUtc = DateTimeOffset.UtcNow,
        vehicles,
        routes,
        eta
    });
});

app.MapReverseProxy();
app.MapFallbackToFile("index.html");
app.Run();

static JsonElement EmptyArray() => JsonSerializer.SerializeToElement(Array.Empty<object>());
