using System.Text.Json;

namespace Avtomagazin.Mobile;

public static class FavoriteStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static List<FavoriteItem> All()
    {
        try
        {
            return JsonSerializer.Deserialize<List<FavoriteItem>>(Preferences.Default.Get("favorites", "[]"), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static bool Contains(Guid stopId) => All().Any(x => x.Id == stopId);

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

    private static void Save(List<FavoriteItem> items)
        => Preferences.Default.Set("favorites", JsonSerializer.Serialize(items));
}

public sealed record FavoriteItem(Guid Id, string SettlementName);
