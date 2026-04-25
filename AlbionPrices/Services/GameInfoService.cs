using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class GameInfoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<ServerRegion, string> ServerUrls = new()
    {
        [ServerRegion.Americas] = "https://gameinfo.albiononline.com/api/gameinfo",
        [ServerRegion.Europe]   = "https://gameinfo-ams.albiononline.com/api/gameinfo",
        [ServerRegion.Asia]     = "https://gameinfo-sgp.albiononline.com/api/gameinfo",
    };

    private readonly HttpClient _httpClient;

    // Cached after a successful search so subsequent calls go direct instead of racing.
    private string? _cachedBaseUrl;

    public ServerRegion Region { get; set; } = ServerRegion.Europe;

    public GameInfoService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");
    }

    private async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    // Races all three regional servers and returns (json, winning base URL).
    private async Task<(string? Json, string? BaseUrl)> RaceWithBaseAsync(
        string path, CancellationToken ct, Func<string, bool>? validate = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = ServerUrls.Values
            .Select(base_ => (Base: base_, Task: FetchAsync(base_ + path, cts.Token)))
            .ToList();

        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending.Select(p => p.Task));
            var item = pending.First(p => p.Task == done);
            pending.Remove(item);
            var result = await done;
            if (result != null && (validate == null || validate(result)))
            {
                cts.Cancel();
                return (result, item.Base);
            }
        }
        ct.ThrowIfCancellationRequested();
        return (null, null);
    }

    // Uses cached server if available, falls back to race.
    private async Task<string?> FetchCachedOrRaceAsync(string path, CancellationToken ct)
    {
        if (_cachedBaseUrl != null)
        {
            var direct = await FetchAsync(_cachedBaseUrl + path, ct);
            if (direct != null) return direct;
        }
        var (json, base_) = await RaceWithBaseAsync(path, ct);
        if (base_ != null) _cachedBaseUrl = base_;
        return json;
    }

    public async Task<GameInfoSearchResult?> SearchAsync(string query, CancellationToken ct = default, Action<int>? onRetry = null)
    {
        _cachedBaseUrl = null;
        var regionUrl = ServerUrls[Region];
        var path = $"/search?q={Uri.EscapeDataString(query)}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                onRetry?.Invoke(attempt);
                await Task.Delay(700, ct);
            }

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(8));

            var json = await FetchAsync(regionUrl + path, attemptCts.Token);
            if (json != null && json.Contains("\"Id\"", StringComparison.OrdinalIgnoreCase))
            {
                _cachedBaseUrl = regionUrl;
                Debug.WriteLine($"[GameInfo] Search '{query}' on {regionUrl} (attempt {attempt + 1})");
                return JsonSerializer.Deserialize<GameInfoSearchResult>(json, JsonOptions);
            }

            Debug.WriteLine($"[GameInfo] Search '{query}' attempt {attempt + 1} failed on {regionUrl}, retrying...");
        }

        return null;
    }

    public async Task<PlayerInfo?> GetPlayerAsync(string id, CancellationToken ct = default)
    {
        var json = await FetchCachedOrRaceAsync($"/players/{id}", ct);
        if (json is null) return null;
        Debug.WriteLine($"[GameInfo] /players/{id} from {_cachedBaseUrl}");
        return JsonSerializer.Deserialize<PlayerInfo>(json, JsonOptions);
    }

    public async Task<List<KillEvent>?> GetPlayerKillsAsync(string id, CancellationToken ct = default)
    {
        var json = await FetchCachedOrRaceAsync($"/players/{id}/kills?limit=10", ct);
        return json is null ? null : JsonSerializer.Deserialize<List<KillEvent>>(json, JsonOptions);
    }

    public async Task<List<KillEvent>?> GetPlayerDeathsAsync(string id, CancellationToken ct = default)
    {
        var json = await FetchCachedOrRaceAsync($"/players/{id}/deaths?limit=10", ct);
        return json is null ? null : JsonSerializer.Deserialize<List<KillEvent>>(json, JsonOptions);
    }

    public static string GetItemIconUrl(string itemId) =>
        $"https://render.albiononline.com/v1/item/{itemId}.png";

    public static string GetPlayerAvatarUrl(string playerName) =>
        $"https://render.albiononline.com/v1/player/{Uri.EscapeDataString(playerName)}/avatar";
}
