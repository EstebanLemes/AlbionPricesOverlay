using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionPrices.Services;

public class ItemDatabase
{
    private List<ItemEntry> _items = new();
    private bool _loaded;
    private string? _loadError;

    public string? LoadError => _loadError;
    public int ItemCount => _items.Count;
    public bool IsLoaded => _loaded;

    // Locales tried in order for the display name
    private static readonly string[] LocalePriority = { "ES-ES", "PT-BR", "EN-US" };

    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");
            client.Timeout = TimeSpan.FromSeconds(180);

            // Try multiple known-good URLs for the Albion items database
            string[] urls =
            [
                "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/refs/heads/master/formatted/items.json",
                "https://raw.githubusercontent.com/broderickhyman/ao-bin-dumps/master/formatted/items.json",
            ];

            string? json = null;
            foreach (var url in urls)
            {
                try
                {
                    var candidate = await client.GetStringAsync(url);
                    System.Diagnostics.Debug.WriteLine($"{url} → {candidate.Length} chars");
                    // Accept the response with the most data
                    if (json == null || candidate.Length > json.Length)
                        json = candidate;
                    if (json.Length > 500_000) break; // good enough, stop trying
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed {url}: {ex.Message}"); }
            }

            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("No se pudo descargar la base de datos.");

            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("Respuesta vacía del servidor.");

            // The file is a JSON array, not a dictionary
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var item in root.EnumerateArray())
            {
                var uniqueName = item.TryGetProperty("UniqueName", out var un) ? un.GetString() : null;
                if (string.IsNullOrEmpty(uniqueName)) continue;

                string? displayName = null;
                var searchNames = new List<string>();

                string? nameEn = null;
                if (item.TryGetProperty("LocalizedNames", out var locNames) && locNames.ValueKind == JsonValueKind.Object)
                {
                    foreach (var locale in LocalePriority)
                    {
                        if (locNames.TryGetProperty(locale, out var n) && n.ValueKind == JsonValueKind.String)
                        {
                            var s = n.GetString();
                            if (!string.IsNullOrEmpty(s))
                            {
                                displayName ??= s;
                                searchNames.Add(s);
                            }
                        }
                    }

                    if (locNames.TryGetProperty("EN-US", out var enVal) && enVal.ValueKind == JsonValueKind.String)
                        nameEn = enVal.GetString();

                    // Fallback: any locale
                    if (displayName == null)
                    {
                        foreach (var prop in locNames.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                displayName = prop.Value.GetString();
                                if (!string.IsNullOrEmpty(displayName))
                                {
                                    searchNames.Add(displayName);
                                    break;
                                }
                            }
                        }
                    }
                }

                displayName ??= uniqueName;

                _items.Add(new ItemEntry
                {
                    Name        = displayName,
                    NameEn      = nameEn ?? "",
                    UniqueName  = uniqueName,
                    SearchNames = searchNames,
                });
            }

            System.Diagnostics.Debug.WriteLine($"Loaded {_items.Count} items from {json!.Length} chars");
            _loaded = true;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"DB Error: {ex}");
            _loaded = true;
        }
    }

    public string? FindIdByName(string searchName)
    {
        if (!_loaded || string.IsNullOrWhiteSpace(searchName)) return null;

        var needle = Normalize(searchName);

        // Exact match first
        foreach (var item in _items)
        {
            foreach (var name in item.SearchNames)
            {
                if (Normalize(name) == needle)
                    return item.UniqueName;
            }
        }

        // Substring match
        foreach (var item in _items)
        {
            foreach (var name in item.SearchNames)
            {
                var n = Normalize(name);
                if (n.Contains(needle) || needle.Contains(n))
                    return item.UniqueName;
            }
        }

        return null;
    }

    public List<string> Search(string query)
    {
        var results = new List<string>();
        if (!_loaded || string.IsNullOrWhiteSpace(query)) return results;

        var needle = Normalize(query);

        foreach (var item in _items)
        {
            foreach (var name in item.SearchNames)
            {
                if (Normalize(name).Contains(needle))
                {
                    results.Add(item.UniqueName);
                    break;
                }
            }

            if (results.Count >= 10) break;
        }

        return results;
    }

    public string? GetNameById(string uniqueName)
    {
        var item = _items.FirstOrDefault(x => x.UniqueName == uniqueName);
        return item?.Name;
    }

    public string? GetEnglishNameById(string uniqueName)
    {
        var item = _items.FirstOrDefault(x => x.UniqueName == uniqueName);
        if (item == null) return null;
        return string.IsNullOrEmpty(item.NameEn) ? item.Name : item.NameEn;
    }

    // Returns all UniqueNames that share the same base item id (ignoring tier prefix and @enchant suffix)
    // e.g. baseId="HEAD_CLOTH_AVALON" → [T4_HEAD_CLOTH_AVALON, T5_HEAD_CLOTH_AVALON@1, ...]
    public List<string> GetVariants(string baseId)
    {
        var results = new List<string>();
        var suffix = $"_{baseId}";
        foreach (var item in _items)
        {
            var name = item.UniqueName;
            // Quick filter before regex
            if (!name.Contains(suffix)) continue;
            // Must match T\d_baseId(@\d)?  exactly
            var atIdx = name.IndexOf('@');
            var nameBase = atIdx >= 0 ? name[..atIdx] : name;
            if (nameBase.Length < 3) continue;
            if (nameBase[0] != 'T' || !char.IsDigit(nameBase[1]) || nameBase[2] != '_') continue;
            if (nameBase[3..] == baseId)
                results.Add(name);
        }
        return results;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var formD = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(formD.Length);

        foreach (var c in formD)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString()
            .Replace("'", "").Replace(".", "").Replace(",", "")
            .Replace("(", "").Replace(")", "").Replace("-", " ").Replace("_", " ")
            .ToLowerInvariant()
            .Trim();
    }
}

public class ItemEntry
{
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public List<string> SearchNames { get; set; } = new();
}