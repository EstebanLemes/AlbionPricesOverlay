using System.Net.Http;
using System.Text.Json;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class AlbionApiService
{
    private readonly HttpClient _httpClient;

    public AlbionApiService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ItemPriceSummary?> GetItemPriceAsync(string itemId)
    {
        try
        {
            var url = $"https://west.albion-online-data.com/api/v2/stats/prices/{itemId}.json";

            System.Diagnostics.Debug.WriteLine($"API URL: {url}");

            var response = await _httpClient.GetStringAsync(url);
            System.Diagnostics.Debug.WriteLine($"API Response: {response[..Math.Min(300, response.Length)]}");

            if (string.IsNullOrWhiteSpace(response) || response == "[]" || response.StartsWith("<"))
                return null;

            var prices = JsonSerializer.Deserialize<List<PriceApiResponse>>(response);
            if (prices == null || prices.Count == 0)
                return null;

            var summary = new ItemPriceSummary { ItemId = itemId, ItemName = itemId };

            // Group by city — API returns one entry per (city, quality), keep best prices per city
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching price: {ex.Message}");
            return null;
        }
    }
}