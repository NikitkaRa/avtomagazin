using System.Text.Json;
using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public static class FavoriteStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static List<FavoriteItem> All()
    {
        try
        {
            return JsonSerializer.Deserialize<List<FavoriteItem>>(Preferences.Default.Get(Key(), "[]"), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static bool Contains(Guid stopId) => All().Any(x => x.Id == stopId);

    public static void Replace(IEnumerable<FavoriteItem> items) => Save(items.ToList());

    public static bool Toggle(Guid stopId, string settlementName)
    {
        var items = All();
        var existing = items.FirstOrDefault(x => x.Id == stopId);
        if (existing is not null)
        {
            items.Remove(existing);
            Save(items);
            return false;
        }

        items.Add(new FavoriteItem(stopId, settlementName));
        Save(items);
        return true;
    }

    public static async Task PullAsync(AvtomagazinClient api)
    {
        var remote = await api.GetFavoritesAsync();
        Replace(remote.Select(x => new FavoriteItem(x.StopId, x.SettlementName)));
    }

    private static void Save(List<FavoriteItem> items)
        => Preferences.Default.Set(Key(), JsonSerializer.Serialize(items));

    private static string Key()
    {
        var userId = Preferences.Default.Get("userId", "anon");
        return $"favorites:{userId}";
    }
}

public sealed record FavoriteItem(Guid Id, string SettlementName);
