using System.Text.Json;
using Avtomagazin.ApiClient;

namespace Avtomagazin.Mobile;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SnapshotDto Current { get; private set; } = new(null, [], [], []);
    public string Source { get; private set; } = "empty";
    public bool Online { get; private set; }

    public IEnumerable<RouteStopDto> AllStops()
        => Current.Routes.SelectMany(r => r.Stops ?? []);

    public RouteStopDto? FindStop(Guid id)
        => AllStops().FirstOrDefault(s => s.Id == id);

    public async Task LoadBootstrapAsync()
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("bootstrap.json");
            Current = await JsonSerializer.DeserializeAsync<SnapshotDto>(stream, Json) ?? Current;
            Source = "bootstrap";
        }
        catch
        {
            Current = new SnapshotDto(null, [], [], []);
            Source = "empty";
        }
    }

    public void LoadCache()
    {
        var raw = Preferences.Default.Get("snapshot", "");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<SnapshotDto>(raw, Json) ?? Current;
            Source = "cache";
        }
        catch
        {
            // keep bootstrap
        }
    }

    public async Task RefreshAsync(AvtomagazinClient api)
    {
        try
        {
            var live = await api.GetSnapshotAsync();
            if (live.Routes.Count == 0 && live.Vehicles.Count == 0)
            {
                throw new InvalidOperationException("empty snapshot");
            }

            Current = live;
            Source = "live";
            Online = true;
            Preferences.Default.Set("snapshot", JsonSerializer.Serialize(live, Json));
        }
        catch
        {
            Online = false;
            if (Current.Routes.Count == 0)
            {
                LoadCache();
            }
        }
    }
}
