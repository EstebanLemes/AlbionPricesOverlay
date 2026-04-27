namespace AlbionPrices.Models;

public class WatchlistEntry
{
    public string   ItemId    { get; set; } = "";
    public string   ItemName  { get; set; } = "";
    public int      Quality   { get; set; } = 1;
    public double   BasePrice { get; set; }   // price at time of adding (for % change)
    public double?  AlertBuyBelow { get; set; }

    // Runtime — not persisted
    [System.Text.Json.Serialization.JsonIgnore]
    public double  LastBuyPrice  { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public double  LastSellPrice { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string  BestBuyCity   { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool    AlertTriggered { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool    IsLoading     { get; set; }
}

public class CraftMaterial
{
    public string ItemId   { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int    Quantity { get; set; } = 1;
    public double UnitPrice { get; set; }
    public string BestCity  { get; set; } = "";
}
