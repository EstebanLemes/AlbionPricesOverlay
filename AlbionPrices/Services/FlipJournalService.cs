using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionPrices.Services;

public class FlipJournalEntry
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = "";

    [JsonPropertyName("tierLabel")]
    public string TierLabel { get; set; } = "";

    [JsonPropertyName("buyCity")]
    public string BuyCity { get; set; } = "";

    [JsonPropertyName("buyPrice")]
    public double BuyPrice { get; set; }

    [JsonPropertyName("sellCity")]
    public string SellCity { get; set; } = "";

    [JsonPropertyName("sellPrice")]
    public double SellPrice { get; set; }

    [JsonPropertyName("expectedProfit")]
    public double ExpectedProfit { get; set; }

    [JsonIgnore]
    public string DateLabel => Date.ToLocalTime().ToString("dd/MM HH:mm");

    [JsonIgnore]
    public string RouteLabel => $"{BuyCity} → {SellCity}";

    [JsonIgnore]
    public string ProfitLabel => ExpectedProfit >= 0 ? $"+{ExpectedProfit:N0}" : $"{ExpectedProfit:N0}";
}

public class FlipJournalService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPrices", "flip_journal.json");

    private const int MaxEntries = 50;

    public List<FlipJournalEntry> Entries { get; private set; } = new();

    public double TotalProfit => Entries.Sum(e => e.ExpectedProfit);

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonSerializer.Deserialize<List<FlipJournalEntry>>(File.ReadAllText(FilePath));
            if (loaded != null) Entries = loaded;
        }
        catch { }
    }

    public void Add(FlipJournalEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries)
            Entries = Entries.Take(MaxEntries).ToList();
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
