namespace AlbionPrices.Models;

public class IslandConfig
{
    public string City { get; set; } = "";
    public int    Tier { get; set; } = 4;

    public int    Plots => Tier switch { 2 => 4, 3 => 6, 4 => 9, 5 => 12, 6 => 16, _ => 4 };
    public string Label => $"{City} T{Tier}";
}

public enum FarmItemType { Cultivo, Hierba, Animal }
public enum FarmPriority { Alta, Media }

public class FarmingBonus
{
    public string       ItemId         { get; set; } = "";
    public string       NameEs         { get; set; } = "";
    public int          Tier           { get; set; }
    public FarmItemType Type           { get; set; }
    public FarmPriority Priority       { get; set; } = FarmPriority.Alta;
    public string       Notes          { get; set; } = "";

    // Animals only
    public string? ProductItemId   { get; set; }
    public string? ProductNameEs   { get; set; }
    public bool    HasButcherBonus { get; set; }
    public string? FavFoodId       { get; set; }   // Item ID of favorite food
    public string? FoodBonusCity   { get; set; }   // City that has bonus for that food
}

public class RecipeIngredient
{
    public string ItemId    { get; set; } = "";
    public string NameEs    { get; set; } = "";
    public int    Qty       { get; set; } = 1;
    public string BonusCity { get; set; } = "";
}

public class FarmingRecipe
{
    public string  OutputId          { get; set; } = "";
    public string  OutputName        { get; set; } = "";
    public int     OutputQty         { get; set; } = 1;
    public string  Category          { get; set; } = "";   // "Pocion" | "Comida"
    public string? CraftingBonusCity { get; set; }         // City with crafting bonus
    public List<RecipeIngredient> Ingredients { get; set; } = new();
}

public enum SynergyType { AlimentoAnimal, Receta, BonusCrafteo }

public class FarmingSynergy
{
    public SynergyType      Type             { get; set; }
    public string           Title            { get; set; } = "";
    public string           Description      { get; set; } = "";
    public List<string>     Cities           { get; set; } = new();
    public FarmingRecipe?   Recipe           { get; set; }
    public bool             IsFullyCovered   { get; set; }
    public List<string>     MissingIngredients { get; set; } = new();
}
