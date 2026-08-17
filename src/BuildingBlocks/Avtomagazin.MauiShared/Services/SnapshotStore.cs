using System.Diagnostics;
using System.Text.Json;
using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

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
    public string? LastError { get; private set; }
    public TimeSpan LastRefreshDuration { get; private set; }
    public DateTimeOffset? LastOnlineAtUtc { get; private set; }

    public IEnumerable<RouteStopDto> AllStops()
        => Current.Routes.SelectMany(r => r.Stops ?? []);

    public IReadOnlyList<DriverNoteDto> ActiveNotes()
        => Current.DriverNotes ?? [];

    public RouteStopDto? FindStop(Guid id)
        => AllStops().FirstOrDefault(s => s.Id == id);

    public async Task LoadBootstrapAsync()
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("bootstrap.json");
            Current = await JsonSerializer.DeserializeAsync<SnapshotDto>(stream, Json) ?? Current;
            Current = Normalize(Current);
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
            Current = Normalize(JsonSerializer.Deserialize<SnapshotDto>(raw, Json) ?? Current);
            Source = "cache";
        }
        catch
        {
            // keep bootstrap
        }
    }

    public async Task RefreshAsync(AvtomagazinClient api)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var live = await api.GetSnapshotAsync();
            Current = Normalize(live);
            Source = "live";
            Online = true;
            LastError = null;
            LastOnlineAtUtc = DateTimeOffset.UtcNow;
            Preferences.Default.Set("snapshot", JsonSerializer.Serialize(live, Json));
        }
        catch (Exception ex)
        {
            Online = false;
            LastError = ShortError(ex);
            if (Current.Routes.Count == 0 && Current.Vehicles.Count == 0)
            {
                LoadCache();
            }
        }
        finally
        {
            sw.Stop();
            LastRefreshDuration = sw.Elapsed;
        }
    }

    private static SnapshotDto Normalize(SnapshotDto snap)
        => snap with { DriverNotes = snap.DriverNotes ?? [] };

    private static string ShortError(Exception ex)
    {
        var msg = ex.GetBaseException().Message;
        if (msg.Contains("502", StringComparison.Ordinal) || msg.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase))
        {
            return "сервер временно недоступен";
        }

        if (msg.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Name or service", StringComparison.OrdinalIgnoreCase))
        {
            return "нет ответа от сервера";
        }

        return msg.Length > 80 ? msg[..80] + "…" : msg;
    }
}
