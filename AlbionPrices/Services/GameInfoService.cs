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
    ];

    private readonly HttpClient _httpClient;

    public GameInfoService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AlbionPrices/1.0");
    }

    public async Task<GameInfoSearchResult?> SearchAsync(string query, CancellationToken ct = default)
    {
        foreach (var baseUrl in BaseUrls)
        {
            try
            {
                var url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<GameInfoSearchResult>(json);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Debug.WriteLine($"GameInfo search error ({baseUrl}): {ex.Message}");
            }
        }
        return null;
    }

    public async Task<PlayerInfo?> GetPlayerAsync(string id, CancellationToken ct = default)
    {
        foreach (var baseUrl in BaseUrls)
        {
            try
            {
                var url = $"{baseUrl}/players/{id}";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<PlayerInfo>(json);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Debug.WriteLine($"GameInfo player error ({baseUrl}): {ex.Message}");
            }
        }
        return null;
    }

    public async Task<List<KillEvent>?> GetPlayerKillsAsync(string id, CancellationToken ct = default)
    {
        foreach (var baseUrl in BaseUrls)
        {
            try
            {
                var url = $"{baseUrl}/players/{id}/kills";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) continue;
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<List<KillEvent>>(json);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Debug.WriteLine($"GameInfo kills error ({baseUrl}): {ex.Message}");
            }
        }
        return null;
    }

    public static string GetItemIconUrl(string itemId) =>
        $"https://render.albiononline.com/v1/item/{itemId}.png";

    public static string GetPlayerAvatarUrl(string playerName) =>
        $"https://render.albiononline.com/v1/player/{Uri.EscapeDataString(playerName)}/avatar";
}
