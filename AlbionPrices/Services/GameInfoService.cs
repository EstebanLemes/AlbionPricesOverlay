using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class GameInfoService
{
    private static readonly string[] BaseUrls =
    [
        "https://gameinfo.albiononline.com/api/gameinfo",
        "https://gameinfo-ams.albiononline.com/api/gameinfo",
        "https://gameinfo-sgp.albiononline.com/api/gameinfo",
    ];

    private readonly HttpClient _httpClient;

    public GameInfoService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");
    }

    // Sends the same request to all servers in parallel and returns the first successful response.
    private async Task<string?> RaceAsync(string path, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tasks = BaseUrls
            .Select(base_ => FetchAsync(base_ + path, cts.Token))
            .ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            try
            {
                var result = await done;
                if (result != null)
                {
                    cts.Cancel();
                    return result;
                }
            }
            catch { }
        }
        return null;
    }

    private async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Debug.WriteLine($"GameInfo fetch error ({url}): {ex.Message}");
            return null;
        }
    }

    public async Task<GameInfoSearchResult?> SearchAsync(string query, CancellationToken ct = default)
    {
        var json = await RaceAsync($"/search?q={Uri.EscapeDataString(query)}", ct);
        return json is null ? null : JsonSerializer.Deserialize<GameInfoSearchResult>(json);
    }

    public async Task<PlayerInfo?> GetPlayerAsync(string id, CancellationToken ct = default)
    {
        var json = await RaceAsync($"/players/{id}", ct);
        return json is null ? null : JsonSerializer.Deserialize<PlayerInfo>(json);
    }

    public async Task<List<KillEvent>?> GetPlayerKillsAsync(string id, CancellationToken ct = default)
    {
        var json = await RaceAsync($"/players/{id}/kills", ct);
        return json is null ? null : JsonSerializer.Deserialize<List<KillEvent>>(json);
    }

    public static string GetItemIconUrl(string itemId) =>
        $"https://render.albiononline.com/v1/item/{itemId}.png";

    public static string GetPlayerAvatarUrl(string playerName) =>
        $"https://render.albiononline.com/v1/player/{Uri.EscapeDataString(playerName)}/avatar";
}
