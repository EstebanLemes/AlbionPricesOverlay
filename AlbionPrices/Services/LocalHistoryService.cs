using System.IO;
using System.Text.Json;

namespace AlbionPrices.Services;

public class HistoryEntry
{
    public string ItemId   { get; set; } = "";
    public string ItemName { get; set; } = "";
}

public class LocalHistoryService
{
    private static readonly string AppDataDir    = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlbionPrices");
    private static readonly string HistoryPath   = Path.Combine(AppDataDir, "history.json");
    private static readonly string FavoritesPath = Path.Combine(AppDataDir, "favorites.json");

    public List<HistoryEntry>    RecentItems { get; private set; } = new();
    public List<HistoryEntry>    Favorites   { get; private set; } = new();

    public LocalHistoryService() => Load();

    public void AddToHistory(string itemId, string itemName)
    {
        RecentItems.RemoveAll(e => e.ItemId == itemId);
        RecentItems.Insert(0, new HistoryEntry { ItemId = itemId, ItemName = itemName });
        if (RecentItems.Count > 10) RecentItems.RemoveAt(10);
        Save();
    }

    public bool ToggleFavorite(string itemId, string itemName)
    {
        var existing = Favorites.FirstOrDefault(e => e.ItemId == itemId);
        if (existing != null)
        {
            Favorites.Remove(existing);
            Save();
            return false;
        }
        Favorites.Add(new HistoryEntry { ItemId = itemId, ItemName = itemName });
        Save();
        return true;
    }

    public bool IsFavorite(string itemId) => Favorites.Any(e => e.ItemId == itemId);

    public void ClearHistory()
    {
        RecentItems.Clear();
        Save();
    }

    private void Load()
    {
        try { if (File.Exists(HistoryPath))   RecentItems = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryPath))   ?? new(); } catch { }
        try { if (File.Exists(FavoritesPath)) Favorites   = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FavoritesPath)) ?? new(); } catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(HistoryPath,   JsonSerializer.Serialize(RecentItems));
            File.WriteAllText(FavoritesPath, JsonSerializer.Serialize(Favorites));
        }
        catch { }
    }
}
