using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionPrices.Models;

public enum ServerRegion { Americas, Europe, Asia }

public class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServerRegion Region { get; set; } = ServerRegion.Europe;

    public bool    FlipPremium      { get; set; } = false;
    public string? FlipOrigin       { get; set; }
    public string? FlipDest         { get; set; }
    public double  FlipMinProfit    { get; set; } = 1000;
    public string  FlipCategory     { get; set; } = "Todo";
    public double  FlipTransportPct { get; set; } = 0;
    public bool    CraftPremium     { get; set; } = false;
    public List<IslandConfig> Islands { get; set; } = [];

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPrices", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
