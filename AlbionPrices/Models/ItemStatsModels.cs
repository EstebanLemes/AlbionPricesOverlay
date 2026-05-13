namespace AlbionPrices.Models;

public class ItemStatsSummary
{
    public string ItemId { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Slot { get; set; } = "";
    public string TierLabel { get; set; } = "";
    public string EnchantLabel { get; set; } = "";
    public string QualityLabel { get; set; } = "";
    public string ItemPowerLabel { get; set; } = "";
    public string VariantLabel { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public string ExternalUrl { get; set; } = "";
    public List<ItemStatLine> Lines { get; set; } = new();
    public List<ItemStatSection> Sections { get; set; } = new();
}

public record ItemStatLine(string Label, string Value);

public class ItemStatSection
{
    public string Title { get; set; } = "";
    public List<ItemStatLine> Lines { get; set; } = new();
}
