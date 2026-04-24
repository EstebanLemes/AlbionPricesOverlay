using System.Net.Http;
using System.Text.Json;
using System.Threading;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class AlbionApiService
{
    private readonly HttpClient _httpClient;

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
    }

    public async Task<ItemPriceSummary?> GetItemPriceAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://west.albion-online-data.com/api/v2/stats/prices/{itemId}.json";

            System.Diagnostics.Debug.WriteLine($"API URL: {url}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            var response = await _httpClient.GetStringAsync(url, cts.Token);

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
                var buyAt = group.Where(p => p.SellPriceMin > 0).Min(p => p.SellPriceMin) ?? 0;
                var sellAt = group.Where(p => p.BuyPriceMax > 0).Max(p => p.BuyPriceMax) ?? 0;

                if (buyAt > 0 || sellAt > 0)
                {
                    summary.Prices.Add(new CityPrices
                    {
                        City = group.Key,
                        BuyAt = buyAt,
                        SellAt = sellAt,
                    });
                }
            }

            return summary.Prices.Count > 0 ? summary : null;
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
}