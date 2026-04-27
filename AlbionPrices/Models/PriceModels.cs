using System.ComponentModel;
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

    [JsonPropertyName("sell_price_min")]
    public double? SellPriceMin { get; set; }

    [JsonPropertyName("sell_price_min_date")]
    public DateTime? SellPriceMinDate { get; set; }

    [JsonPropertyName("sell_price_max")]
    public double? SellPriceMax { get; set; }

    [JsonPropertyName("buy_price_min")]
    public double? BuyPriceMin { get; set; }

    [JsonPropertyName("buy_price_max")]
    public double? BuyPriceMax { get; set; }

    [JsonPropertyName("buy_price_max_date")]
    public DateTime? BuyPriceMaxDate { get; set; }

    [JsonPropertyName("sell_amount")]
    public int? SellAmount { get; set; }

    [JsonPropertyName("buy_amount")]
    public int? BuyAmount { get; set; }
}

public class CityPrices
{
    public string City { get; set; } = string.Empty;
    public double BuyAt { get; set; }
    public DateTime? BuyAtDate { get; set; }
    public double SellAt { get; set; }
    public DateTime? SellAtDate { get; set; }
}

public class ItemPriceSummary
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public List<CityPrices> Prices { get; set; } = new();

    // Cheapest sell order = lowest price to buy the item
    public CityPrices? BestBuyCity => Prices.Where(p => p.BuyAt > 0).OrderBy(p => p.BuyAt).FirstOrDefault();
    // Most expensive sell order = best reference price to list your own sell order
    public CityPrices? BestSellOrderCity => Prices.Where(p => p.BuyAt > 0).OrderByDescending(p => p.BuyAt).FirstOrDefault();
    // Highest buy order = best price for an instant sale to an existing buyer
    public CityPrices? BestInstantSellCity => Prices.Where(p => p.SellAt > 0).OrderByDescending(p => p.SellAt).FirstOrDefault();
}

public class GoldPriceEntry
{
    [JsonPropertyName("price")]
    public long Price { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

public class PriceHistoryEntry
{
    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("quality")]
    public int Quality { get; set; }

    [JsonPropertyName("data")]
    public List<PriceHistoryPoint>? Data { get; set; }
}

public class PriceHistoryPoint
{
    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("avg_price")]
    public long AvgPrice { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

public class CityPriceViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private double _buyAt;
    private double _sellAt;
    private DateTime? _buyAtDate;
    private DateTime? _sellAtDate;

    public string City { get; set; } = "";

    public double BuyAt
    {
        get => _buyAt;
        set { _buyAt = value; Notify(nameof(BuyAtLabel)); }
    }

    public double SellAt
    {
        get => _sellAt;
        set { _sellAt = value; Notify(nameof(SellAtLabel)); }
    }

    public DateTime? BuyAtDate
    {
        get => _buyAtDate;
        set { _buyAtDate = value; Notify(nameof(BuyAtAgo)); }
    }

    public DateTime? SellAtDate
    {
        get => _sellAtDate;
        set { _sellAtDate = value; Notify(nameof(SellAtAgo)); }
    }

    public string BuyAtLabel  => BuyAt  > 0 ? $"{BuyAt:N0}"  : "—";
    public string SellAtLabel => SellAt > 0 ? $"{SellAt:N0}" : "—";
    public string BuyAtAgo    => FormatAgo(BuyAtDate);
    public string SellAtAgo   => FormatAgo(SellAtDate);

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string FormatAgo(DateTime? date)
    {
        if (date == null || date == DateTime.MinValue) return "";
        var diff = DateTime.UtcNow - date.Value.ToUniversalTime();
        if (diff.TotalSeconds < 60) return "ahora";
        if (diff.TotalMinutes < 60) return $"hace {(int)diff.TotalMinutes}m";
        if (diff.TotalHours < 24)   return $"hace {(int)diff.TotalHours}h";
        return $"hace {(int)diff.TotalDays}d";
    }
}
