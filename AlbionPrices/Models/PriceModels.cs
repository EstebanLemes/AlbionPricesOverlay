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
    public int SellAmount { get; set; }
    public int BuyAmount  { get; set; }
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
    private int _sellAmount;
    private int _buyAmount;

    public string City { get; set; } = "";

    public double BuyAt
    {
        get => _buyAt;
        set { _buyAt = value; Notify(nameof(BuyAtLabel)); Notify(nameof(BuyAtFull)); }
    }

    public double SellAt
    {
        get => _sellAt;
        set { _sellAt = value; Notify(nameof(SellAtLabel)); Notify(nameof(SellAtFull)); }
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

    public int SellAmount
    {
        get => _sellAmount;
        set { _sellAmount = value; Notify(nameof(SellAmountLabel)); Notify(nameof(BuyAtFull)); }
    }

    public int BuyAmount
    {
        get => _buyAmount;
        set { _buyAmount = value; Notify(nameof(BuyAmountLabel)); Notify(nameof(SellAtFull)); }
    }

    public string BuyAtLabel      => BuyAt  > 0 ? $"{BuyAt:N0}"  : "—";
    public string SellAtLabel     => SellAt > 0 ? $"{SellAt:N0}" : "—";
    public string BuyAtAgo        => FormatAgo(BuyAtDate);
    public string SellAtAgo       => FormatAgo(SellAtDate);
    public string SellAmountLabel => SellAmount > 0 ? $"×{SellAmount}" : "";
    public string BuyAmountLabel  => BuyAmount  > 0 ? $"×{BuyAmount}"  : "";
    public string BuyAtFull       => BuyAt  > 0 ? (SellAmount > 0 ? $"{BuyAt:N0}  ×{SellAmount}" : $"{BuyAt:N0}") : "—";
    public string SellAtFull      => SellAt > 0 ? (BuyAmount  > 0 ? $"{SellAt:N0}  ×{BuyAmount}"  : $"{SellAt:N0}") : "—";

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

public class FlipOpportunity
{
    public string ItemId    { get; set; } = "";
    public string ItemName  { get; set; } = "";
    public string TierLabel { get; set; } = "";
    public int    Quality   { get; set; } = 1;

    public string BuyCity   { get; set; } = "";
    public double BuyPrice  { get; set; }
    public int    BuyVolume { get; set; }

    // Venta directa (instant sell via orden de compra en destino)
    public string InstantSellCity     { get; set; } = "";
    public double InstantSellPrice    { get; set; }   // BuyPriceMax en destino
    public double InstantSellOrderRef { get; set; }   // SellPriceMin en ese mismo destino
    public int    InstantSellVolume   { get; set; }
    public double InstantProfit       { get; set; }
    public double InstantProfitPct    { get; set; }

    // Orden de venta (colocar sell order en destino)
    public string OrderSellCity      { get; set; } = "";
    public double OrderSellPrice     { get; set; }    // SellPriceMin en destino
    public double OrderSellInstantRef{ get; set; }    // BuyPriceMax en ese mismo destino
    public int    OrderSellVolume    { get; set; }
    public double OrderProfit        { get; set; }
    public double OrderProfitPct     { get; set; }

    public bool HasInstantSell => InstantSellPrice > 0 && InstantProfit > 0;
    public bool HasOrderSell   => OrderSellPrice   > 0 && OrderProfit   > 0;

    // Mejor ganancia de las dos opciones (para ordenar y filtrar)
    public double Profit => Math.Max(HasInstantSell ? InstantProfit : 0,
                                     HasOrderSell   ? OrderProfit   : 0);

    // Para journal: la opción con mayor ganancia
    public string SellCity  => InstantProfit >= OrderProfit ? InstantSellCity  : OrderSellCity;
    public double SellPrice => InstantProfit >= OrderProfit ? InstantSellPrice : OrderSellPrice;

    public string QualityLabel => Quality switch
    {
        2 => "Buena", 3 => "Dest.", 4 => "Exc.", 5 => "Obra", _ => "Nor.",
    };

    public string BuyPriceLabel  => $"{BuyPrice:N0}s";
    public string BuyVolumeLabel => BuyVolume > 0 ? $"×{BuyVolume}" : "";

    public string InstantSellPriceLabel  => $"{InstantSellPrice:N0}s";
    public string InstantSellVolumeLabel => InstantSellVolume > 0 ? $"×{InstantSellVolume}" : "";
    public string InstantSellOrderRefLabel => InstantSellOrderRef > 0 ? $"ord. {InstantSellOrderRef:N0}s" : "";
    public bool   HasInstantSellOrderRef   => InstantSellOrderRef > 0;
    public string InstantProfitLabel     => $"+{InstantProfit:N0}s";
    public string InstantProfitPctLabel  => $"+{InstantProfitPct:F1}%";

    public string OrderSellPriceLabel    => $"{OrderSellPrice:N0}s";
    public string OrderSellVolumeLabel   => OrderSellVolume > 0 ? $"×{OrderSellVolume}" : "";
    public string OrderSellInstantRefLabel => OrderSellInstantRef > 0 ? $"dir. {OrderSellInstantRef:N0}s" : "";
    public bool   HasOrderSellInstantRef   => OrderSellInstantRef > 0;
    public string OrderProfitLabel       => $"+{OrderProfit:N0}s";
    public string OrderProfitPctLabel    => $"+{OrderProfitPct:F1}%";

    public string ProfitLabel => $"+{Profit:N0}s";
}
