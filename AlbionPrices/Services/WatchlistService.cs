using System.IO;
using System.Text.Json;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class WatchlistService
{
    private static readonly string Dir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlbionPrices");
    private static readonly string File = Path.Combine(Dir, "watchlist.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public List<WatchlistEntry> Items { get; private set; } = new();

    public WatchlistService() => Load();

    public bool Add(string itemId, string itemName, int quality, double currentPrice)
    {
        if (Items.Any(e => e.ItemId == itemId && e.Quality == quality)) return false;
        Items.Add(new WatchlistEntry
        {
            ItemId    = itemId,
            ItemName  = itemName,
            Quality   = quality,
            BasePrice = currentPrice,
        });
        Save();
        return true;
    }

    public void Remove(string itemId, int quality)
    {
        Items.RemoveAll(e => e.ItemId == itemId && e.Quality == quality);
        Save();
    }

    public void SetAlert(string itemId, int quality, double? threshold)
    {
        var e = Items.FirstOrDefault(x => x.ItemId == itemId && x.Quality == quality);
        if (e == null) return;
        e.AlertBuyBelow  = threshold;
        e.AlertTriggered = false;
        Save();
    }

    public void UpdatePrices(string itemId, int quality, double buyPrice, double sellPrice, string bestBuyCity)
    {
        var e = Items.FirstOrDefault(x => x.ItemId == itemId && x.Quality == quality);
        if (e == null) return;
        e.LastBuyPrice  = buyPrice;
        e.LastSellPrice = sellPrice;
        e.BestBuyCity   = bestBuyCity;
        e.AlertTriggered = e.AlertBuyBelow.HasValue && buyPrice > 0 && buyPrice <= e.AlertBuyBelow.Value;
    }

    public bool Contains(string itemId, int quality) =>
        Items.Any(e => e.ItemId == itemId && e.Quality == quality);

    private void Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                Items = JsonSerializer.Deserialize<List<WatchlistEntry>>(
                    System.IO.File.ReadAllText(File)) ?? new();
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(Items, Opts));
        }
        catch { }
    }
}
