using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionPrices.Services;

public class ScanItemEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nameEs")]
    public string NameEs { get; set; } = "";

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Todo";
}

public class ScanCatalogService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPrices", "scan_items.json");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public List<ScanItemEntry> Items { get; private set; } = new();

    public static string GetFilePath() => FilePath;

    // IDs without a T#_ prefix are equipment type stubs — expanded to T#_<id>[@enc] at scan time.
    // IDs starting with T#_ are full IDs used as-is (resources, consumables, etc.).
    public static bool IsFullItemId(string id) =>
        id.Length >= 3 && id[0] == 'T' && char.IsDigit(id[1]) && id[2] == '_';

    public bool Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            var loaded = JsonSerializer.Deserialize<List<ScanItemEntry>>(File.ReadAllText(FilePath));
            if (loaded?.Count > 0) { Items = loaded; return true; }
        }
        catch { }
        return false;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, WriteOptions));
        }
        catch { }
    }

    private static readonly string[] DefaultWeaponTypes =
    [
        "MAIN_SWORD", "MAIN_AXE", "MAIN_DAGGER", "MAIN_SPEAR", "MAIN_MACE",
        "MAIN_FIRESTAFF", "MAIN_HOLYSTAFF", "MAIN_NATURESTAFF", "MAIN_ARCANESTAFF",
        "MAIN_CURSEDSTAFF", "MAIN_FROSTSTAFF", "MAIN_BOW",
        "2H_SWORD", "2H_AXE", "2H_HAMMER", "2H_BOW", "2H_CROSSBOW",
        "2H_DAGGERPAIR", "2H_SPEAR", "2H_FIRESTAFF", "2H_HOLYSTAFF",
        "2H_NATURESTAFF", "2H_ARCANESTAFF", "2H_CURSEDSTAFF", "2H_FROSTSTAFF",
        "OFF_SHIELD", "OFF_TOTEM", "OFF_BOOK", "OFF_ORB",
    ];

    private static readonly string[] DefaultArmorTypes =
    [
        "HEAD_PLATE", "ARMOR_PLATE", "SHOES_PLATE",
        "HEAD_LEATHER", "ARMOR_LEATHER", "SHOES_LEATHER",
        "HEAD_CLOTH", "ARMOR_CLOTH", "SHOES_CLOTH",
    ];

    private static readonly string[] DefaultAccTypes = ["BAG", "CAPE"];

    private static readonly string[] DefaultResourceIds =
    [
        "T4_ORE",        "T5_ORE",        "T6_ORE",        "T7_ORE",        "T8_ORE",
        "T4_FIBER",      "T5_FIBER",      "T6_FIBER",      "T7_FIBER",      "T8_FIBER",
        "T4_WOOD",       "T5_WOOD",       "T6_WOOD",       "T7_WOOD",       "T8_WOOD",
        "T4_HIDE",       "T5_HIDE",       "T6_HIDE",       "T7_HIDE",       "T8_HIDE",
        "T4_ROCK",       "T5_ROCK",       "T6_ROCK",       "T7_ROCK",       "T8_ROCK",
        "T4_METALBAR",   "T5_METALBAR",   "T6_METALBAR",   "T7_METALBAR",   "T8_METALBAR",
        "T4_CLOTH",      "T5_CLOTH",      "T6_CLOTH",      "T7_CLOTH",      "T8_CLOTH",
        "T4_PLANKS",     "T5_PLANKS",     "T6_PLANKS",     "T7_PLANKS",     "T8_PLANKS",
        "T4_LEATHER",    "T5_LEATHER",    "T6_LEATHER",    "T7_LEATHER",    "T8_LEATHER",
        "T4_STONEBLOCK", "T5_STONEBLOCK", "T6_STONEBLOCK", "T7_STONEBLOCK", "T8_STONEBLOCK",
        "T4_RUNE",  "T5_RUNE",  "T6_RUNE",  "T7_RUNE",
        "T4_SOUL",  "T5_SOUL",  "T6_SOUL",  "T7_SOUL",
        "T4_RELIC", "T5_RELIC", "T6_RELIC", "T7_RELIC",
    ];

    public void GenerateDefault(ItemDatabase db)
    {
        Items = new List<ScanItemEntry>();

        void Add(string id, string cat)
        {
            var lookupId = IsFullItemId(id) ? id : $"T5_{id}";
            Items.Add(new ScanItemEntry
            {
                Id       = id,
                NameEs   = db.GetNameById(lookupId) ?? id,
                NameEn   = db.GetEnglishNameById(lookupId) ?? id,
                Category = cat,
            });
        }

        foreach (var t in DefaultWeaponTypes)  Add(t, "Armas");
        foreach (var t in DefaultArmorTypes)   Add(t, "Armad.");
        foreach (var t in DefaultAccTypes)     Add(t, "Acces.");
        foreach (var id in DefaultResourceIds) Add(id, "Recursos");
    }
}
