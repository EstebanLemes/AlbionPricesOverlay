using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class AlbionApiService
{
    private static readonly Dictionary<ServerRegion, string> StatsBaseUrls = new()
    {
        [ServerRegion.Americas] = "https://west.albion-online-data.com/api/v2/stats",
        [ServerRegion.Europe]   = "https://europe.albion-online-data.com/api/v2/stats",
        [ServerRegion.Asia]     = "https://east.albion-online-data.com/api/v2/stats",
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate = new(2, 2);
    private readonly ConcurrentDictionary<string, (DateTime StoredAt, ItemPriceSummary? Summary)> _priceCache = new();
    private readonly ConcurrentDictionary<string, (DateTime StoredAt, List<PriceApiResponse>? Rows)> _batchCache = new();
    private readonly ConcurrentDictionary<string, CachedPriceRows> _persistentRows = new();
    private static readonly TimeSpan PriceCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DiskPriceCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly string DiskPriceCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPrices",
        "price_rows_cache.json");

    public ServerRegion Region { get; set; } = ServerRegion.Europe;

    public AlbionApiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");
        LoadDiskPriceCache();
    }

    public async Task<ItemPriceSummary?> GetItemPriceAsync(string itemId, int quality = 1, CancellationToken ct = default)
    {
        var cacheKey = $"{Region}:{itemId}:{quality}";
        if (_priceCache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.StoredAt < PriceCacheTtl)
            return cached.Summary;

        try
        {
            var url = $"{StatsBaseUrls[Region]}/prices/{itemId}.json?qualities={quality}";

            System.Diagnostics.Debug.WriteLine($"API URL: {url}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            var response = await SendStringWithRetryAsync(url, cts.Token);

            ct.ThrowIfCancellationRequested();

            System.Diagnostics.Debug.WriteLine($"API Response: {response[..Math.Min(300, response.Length)]}");

            if (string.IsNullOrWhiteSpace(response) || response == "[]" || response.StartsWith("<"))
                return null;

            var prices = JsonSerializer.Deserialize<List<PriceApiResponse>>(response);
            if (prices == null || prices.Count == 0)
                return null;

            var summary = new ItemPriceSummary { ItemId = itemId, ItemName = itemId };

            foreach (var group in prices.Where(p => p.City != null).GroupBy(p => p.City!))
            {
                var buyAt  = group.Where(p => p.SellPriceMin > 0).Min(p => p.SellPriceMin) ?? 0;
                var sellAt = group.Where(p => p.BuyPriceMax  > 0).Max(p => p.BuyPriceMax)  ?? 0;

                if (buyAt > 0 || sellAt > 0)
                {
                    var buyEntry  = group.Where(p => p.SellPriceMin > 0).OrderBy(p => p.SellPriceMin).FirstOrDefault();
                    var sellEntry = group.Where(p => p.BuyPriceMax  > 0).OrderByDescending(p => p.BuyPriceMax).FirstOrDefault();
                    summary.Prices.Add(new CityPrices
                    {
                        City       = group.Key,
                        BuyAt      = buyAt,
                        BuyAtDate  = buyEntry?.SellPriceMinDate,
                        SellAt     = sellAt,
                        SellAtDate = sellEntry?.BuyPriceMaxDate,
                        SellAmount = group.Sum(p => p.SellAmount ?? 0),
                        BuyAmount  = group.Sum(p => p.BuyAmount  ?? 0),
                    });
                }
            }

            var result = summary.Prices.Count > 0 ? summary : null;
            _priceCache[cacheKey] = (DateTime.UtcNow, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("API request cancelled or timed out");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching price: {ex.Message}");
            return null;
        }
    }

    public async Task<GoldPriceEntry?> GetGoldPriceAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{StatsBaseUrls[Region]}/gold?count=1";
            var response = await _httpClient.GetStringAsync(url, ct);
            var list = JsonSerializer.Deserialize<List<GoldPriceEntry>>(response);
            return list?.FirstOrDefault();
        }
        catch { return null; }
    }

    public async Task<List<PriceApiResponse>?> GetBatchPricesAsync(IEnumerable<string> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return [];

        var cacheKey = $"{Region}:{string.Join(",", ids)}";
        if (_batchCache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.StoredAt < PriceCacheTtl)
            return cached.Rows;

        var cachedRows = new List<PriceApiResponse>();
        var missingIds = new List<string>();
        foreach (var id in ids)
        {
            var itemCacheKey = GetDiskPriceCacheKey(Region, id);
            if (_persistentRows.TryGetValue(itemCacheKey, out var diskCached) &&
                DateTime.UtcNow - diskCached.StoredAt < DiskPriceCacheTtl &&
                diskCached.Rows.Count > 0)
            {
                cachedRows.AddRange(diskCached.Rows);
            }
            else
            {
                missingIds.Add(id);
            }
        }

        if (missingIds.Count == 0)
        {
            _batchCache[cacheKey] = (DateTime.UtcNow, cachedRows);
            return cachedRows;
        }

        try
        {
            var url = $"{StatsBaseUrls[Region]}/prices/{string.Join(",", missingIds)}.json?qualities=1,2,3,4,5";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var response = await SendStringWithRetryAsync(url, cts.Token);
            if (string.IsNullOrWhiteSpace(response) || response == "[]" || response.StartsWith("<"))
                return cachedRows.Count > 0 ? cachedRows : null;
            var rows = JsonSerializer.Deserialize<List<PriceApiResponse>>(response) ?? [];

            foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.ItemId))
                         .GroupBy(r => r.ItemId!, StringComparer.OrdinalIgnoreCase))
            {
                _persistentRows[GetDiskPriceCacheKey(Region, group.Key)] = new CachedPriceRows
                {
                    StoredAt = DateTime.UtcNow,
                    Rows = group.ToList(),
                };
            }
            SaveDiskPriceCache();

            var result = cachedRows.Concat(rows).ToList();
            _batchCache[cacheKey] = (DateTime.UtcNow, result);
            return result;
        }
        catch (OperationCanceledException) { return cachedRows.Count > 0 ? cachedRows : null; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch fetch error: {ex.Message}");
            return cachedRows.Count > 0 ? cachedRows : null;
        }
    }

    private static string GetDiskPriceCacheKey(ServerRegion region, string itemId) =>
        $"{region}:{itemId}".ToUpperInvariant();

    private void LoadDiskPriceCache()
    {
        try
        {
            if (!File.Exists(DiskPriceCachePath)) return;
            var items = JsonSerializer.Deserialize<Dictionary<string, CachedPriceRows>>(File.ReadAllText(DiskPriceCachePath));
            if (items == null) return;
            foreach (var (key, value) in items)
            {
                if (DateTime.UtcNow - value.StoredAt < DiskPriceCacheTtl)
                    _persistentRows[key] = value;
            }
        }
        catch { }
    }

    private void SaveDiskPriceCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiskPriceCachePath)!);
            var fresh = _persistentRows
                .Where(kv => DateTime.UtcNow - kv.Value.StoredAt < DiskPriceCacheTtl)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(DiskPriceCachePath, JsonSerializer.Serialize(fresh));
        }
        catch { }
    }

    private async Task<string> SendStringWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _requestGate.WaitAsync(ct);
            try
            {
                using var response = await _httpClient.GetAsync(url, ct);
                if ((int)response.StatusCode == 429 && attempt < 2)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(800 * (attempt + 1));
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            finally
            {
                _requestGate.Release();
            }
        }
    }

    public async Task<List<PriceHistoryEntry>?> GetPriceHistoryAsync(string itemId, int quality = 1, int timeScale = 24, CancellationToken ct = default)
    {
        try
        {
            var url = $"{StatsBaseUrls[Region]}/history/{itemId}?time-scale={timeScale}&qualities={quality}";
            var response = await _httpClient.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<List<PriceHistoryEntry>>(response);
        }
        catch { return null; }
    }
}

public class CachedPriceRows
{
    public DateTime StoredAt { get; set; }
    public List<PriceApiResponse> Rows { get; set; } = [];
}
