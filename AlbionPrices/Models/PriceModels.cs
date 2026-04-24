using System.Text.Json.Serialization;

namespace AlbionPrices.Models;

public class PriceApiResponse
{
    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("quality")]
    public int? Quality { get; set; }

    // Cheapest sell order = price you PAY to buy the item instantly
    [JsonPropertyName("sell_price_min")]
    public double? SellPriceMin { get; set; }

    [JsonPropertyName("sell_price_max")]
    public double? SellPriceMax { get; set; }

    [JsonPropertyName("buy_price_min")]
    public double? BuyPriceMin { get; set; }

    // Highest buy order = price you GET when selling the item instantly
    [JsonPropertyName("buy_price_max")]
    public double? BuyPriceMax { get; set; }

    [JsonPropertyName("sell_amount")]
    public int? SellAmount { get; set; }

    [JsonPropertyName("buy_amount")]
    public int? BuyAmount { get; set; }
}

public class CityPrices
{
    public string City { get; set; } = string.Empty;
    public double BuyAt { get; set; }   // sell_price_min: price to buy the item
    public double SellAt { get; set; }  // buy_price_max: price received when selling
}

public class ItemPriceSummary
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public List<CityPrices> Prices { get; set; } = new();

    public CityPrices? BestBuyCity => Prices.Where(p => p.BuyAt > 0).OrderBy(p => p.BuyAt).FirstOrDefault();
    public CityPrices? BestSellCity => Prices.Where(p => p.SellAt > 0).OrderByDescending(p => p.SellAt).FirstOrDefault();
}