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
        return SearchDetailed(query, 10).Select(r => r.ItemId).ToList();
    }

    public List<ItemSearchMatch> SearchDetailed(string query, int limit = 10)
    {
        if (!_loaded || string.IsNullOrWhiteSpace(query)) return new();

        var needle = Normalize(query);
        if (needle.Length == 0) return new();

        var bestByFamily = new Dictionary<string, ItemSearchMatch>();
        foreach (var item in _items)
        {
            var bestScore = ScoreName(Normalize(item.UniqueName), needle, isId: true);
            foreach (var name in item.SearchNames)
                bestScore = Math.Max(bestScore, ScoreName(Normalize(name), needle));

            if (bestScore <= 0) continue;
            var match = new ItemSearchMatch(
                item.UniqueName,
                item.Name,
                string.IsNullOrWhiteSpace(item.NameEn) ? null : item.NameEn,
                bestScore);

            var familyKey = GetVariantFamilyKey(item.UniqueName);
            if (!bestByFamily.TryGetValue(familyKey, out var existing) ||
                match.Score > existing.Score ||
                (match.Score == existing.Score && PreferRepresentative(match.ItemId, existing.ItemId)))
            {
                bestByFamily[familyKey] = match;
            }
        }

        return bestByFamily.Values
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Name.Length)
            .Take(limit)
            .ToList();
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

    private static int ScoreName(string candidate, string needle, bool isId = false)
    {
        if (candidate.Length == 0 || needle.Length == 0) return 0;

        var score = 0;
        if (candidate == needle) score = 1000;
        else if (candidate.StartsWith(needle)) score = 850 - Math.Min(candidate.Length - needle.Length, 200);
        else if (candidate.Contains(needle)) score = 650 - Math.Min(candidate.IndexOf(needle, StringComparison.Ordinal), 150);
        else
        {
            var subsequenceScore = ScoreSubsequence(candidate, needle);
            if (subsequenceScore > 0) score = subsequenceScore;

            if (needle.Length >= 4)
            {
                var distance = LevenshteinDistance(candidate, needle, maxDistance: 3);
                if (distance >= 0)
                    score = Math.Max(score, 520 - distance * 80 - Math.Min(candidate.Length, 120));
            }
        }

        return isId && score > 0 ? score - 20 : score;
    }

    private static string GetVariantFamilyKey(string uniqueName)
    {
        var withoutEnchant = uniqueName.Split('@', 2)[0];
        if (withoutEnchant.Length >= 3 &&
            withoutEnchant[0] == 'T' &&
            char.IsDigit(withoutEnchant[1]) &&
            withoutEnchant[2] == '_')
        {
            return withoutEnchant[3..];
        }

        return withoutEnchant;
    }

    private static bool PreferRepresentative(string candidateId, string existingId)
    {
        var candidateEnchant = GetEnchantLevel(candidateId);
        var existingEnchant  = GetEnchantLevel(existingId);
        if (candidateEnchant != existingEnchant)
            return candidateEnchant < existingEnchant;

        var candidateTier = GetTier(candidateId);
        var existingTier  = GetTier(existingId);
        if (candidateTier != existingTier)
            return candidateTier < existingTier;

        return string.CompareOrdinal(candidateId, existingId) < 0;
    }

    private static int GetEnchantLevel(string uniqueName)
    {
        var at = uniqueName.IndexOf('@');
        return at >= 0 && int.TryParse(uniqueName[(at + 1)..], out var enchant) ? enchant : 0;
    }

    private static int GetTier(string uniqueName) =>
        uniqueName.Length >= 2 && uniqueName[0] == 'T' && char.IsDigit(uniqueName[1])
            ? uniqueName[1] - '0'
            : int.MaxValue;

    private static int ScoreSubsequence(string candidate, string needle)
    {
        var pos = -1;
        var gaps = 0;
        foreach (var c in needle)
        {
            var next = candidate.IndexOf(c, pos + 1);
            if (next < 0) return 0;
            if (pos >= 0) gaps += next - pos - 1;
            pos = next;
        }

        return Math.Max(120, 430 - gaps * 8 - candidate.Length);
    }

    private static int LevenshteinDistance(string a, string b, int maxDistance)
    {
        if (Math.Abs(a.Length - b.Length) > maxDistance) return -1;

        var previous = new int[b.Length + 1];
        var current  = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > maxDistance) return -1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= maxDistance ? previous[b.Length] : -1;
    }
}

public record ItemSearchMatch(string ItemId, string Name, string? EnglishName, int Score);

public class ItemEntry
{
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string UniqueName { get; set; } = "";
    public List<string> SearchNames { get; set; } = new();
}
