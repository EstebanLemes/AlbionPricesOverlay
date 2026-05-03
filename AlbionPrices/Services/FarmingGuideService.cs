using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public class FarmingGuideService
{
    // ── Static bonus data per city ────────────────────────────────────────────

    private static readonly Dictionary<string, List<FarmingBonus>> CityBonuses = new()
    {
        ["Thetford"] =
        [
            new() { ItemId = "T5_CABBAGE",             NameEs = "Repollo",             Tier = 5, Type = FarmItemType.Cultivo, Notes = "Comidas T5, alimento de animales" },
            new() { ItemId = "T7_FIRETOUCHED_MULLEIN", NameEs = "Gordolobo de fuego",  Tier = 7, Type = FarmItemType.Hierba,  Notes = "Poción de Veneno Mayor — alta demanda PvP" },
            new() { ItemId = "T2_ARCANE_AGARIC",       NameEs = "Agarico arcano",       Tier = 2, Type = FarmItemType.Hierba,  Priority = FarmPriority.Media, Notes = "Pociones menores, buen relleno de parcelas" },
            new() { ItemId = "T7_PIG",                 NameEs = "Cerdo",               Tier = 7, Type = FarmItemType.Animal,  Priority = FarmPriority.Media,
                    ProductItemId = "T7_RAW_PORK", ProductNameEs = "Carne de cerdo T7",
                    HasButcherBonus = true, FavFoodId = "T7_CORN", FoodBonusCity = "Bridgewatch",
                    Notes = "Sin farming bonus al criar, +10% carne al carnicear en Thetford" },
        ],
        ["Lymhurst"] =
        [
            new() { ItemId = "T8_PUMPKIN",     NameEs = "Calabaza",           Tier = 8, Type = FarmItemType.Cultivo, Notes = "Cultivo máximo tier — alta demanda constante" },
            new() { ItemId = "T4_BURDOCK_ROOT",NameEs = "Bardana almenada",   Tier = 4, Type = FarmItemType.Hierba,  Notes = "Ingrediente Poción de Veneno Menor" },
            new() { ItemId = "T5_GOOSE",       NameEs = "Ganso",              Tier = 5, Type = FarmItemType.Animal,
                    ProductItemId = "T5_GOOSE_EGG", ProductNameEs = "Huevos de ganso T5",
                    HasButcherBonus = true, FavFoodId = "T1_CARROT", FoodBonusCity = "Lymhurst",
                    Notes = "Doble bonus: +10% huevos al criar, +10% carne al carnicear" },
            new() { ItemId = "T1_CARROT",      NameEs = "Zanahoria",          Tier = 1, Type = FarmItemType.Cultivo, Notes = "Comida favorita del ganso — ciclo 100% local" },
        ],
        ["Bridgewatch"] =
        [
            new() { ItemId = "T7_CORN",        NameEs = "Maíz",               Tier = 7, Type = FarmItemType.Cultivo, Notes = "Comida favorita del cerdo y la vaca — alta demanda" },
            new() { ItemId = "T7_DRAGON_TEASEL", NameEs = "Cardo de dragón",   Tier = 7, Type = FarmItemType.Hierba,  Notes = "Ingrediente Poción de Veneno Mayor T7 — alta demanda PvP" },
            new() { ItemId = "T5_TEASEL_ROOT",   NameEs = "Cardo borriqueño",  Tier = 5, Type = FarmItemType.Hierba,  Notes = "Ingrediente Poción de Resistencia — demanda PvP" },
            new() { ItemId = "T4_GOAT",        NameEs = "Cabra",              Tier = 4, Type = FarmItemType.Animal,
                    ProductItemId = "T4_GOATMILK", ProductNameEs = "Leche de cabra T4",
                    HasButcherBonus = true, FavFoodId = "T2_BEAN", FoodBonusCity = "Bridgewatch",
                    Notes = "Doble bonus: +10% leche al criar, +10% carne al carnicear" },
            new() { ItemId = "T2_BEAN",        NameEs = "Alubia",             Tier = 2, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media, Notes = "Comida favorita de la cabra — ciclo local posible" },
        ],
        ["Fort Sterling"] =
        [
            new() { ItemId = "T7_GHOUL_YARROW", NameEs = "Milenrama demoniaca", Tier = 7, Type = FarmItemType.Hierba,  Notes = "Ingrediente Poción de Veneno Mayor T7 — alta demanda PvP" },
            new() { ItemId = "T8_GHOUL_YARROW", NameEs = "Yarrow fantasma T8", Tier = 8, Type = FarmItemType.Hierba,  Notes = "Hierba máximo tier — pociones endgame" },
            new() { ItemId = "T6_SHEEP",        NameEs = "Oveja",             Tier = 6, Type = FarmItemType.Animal,
                    ProductItemId = "T6_SHEEPSMILK", ProductNameEs = "Leche de oveja T6",
                    HasButcherBonus = true, FavFoodId = "T4_TURNIP", FoodBonusCity = "Fort Sterling",
                    Notes = "Doble bonus: +10% leche al criar, +10% carne al carnicear. Ciclo local con Nabo." },
            new() { ItemId = "T4_TURNIP",       NameEs = "Nabo",              Tier = 4, Type = FarmItemType.Cultivo, Notes = "Comida favorita de la oveja — ciclo cerrado" },
            new() { ItemId = "T3_CHICKEN",      NameEs = "Gallina",           Tier = 3, Type = FarmItemType.Animal,  Priority = FarmPriority.Media,
                    ProductItemId = "T3_HEN_EGG", ProductNameEs = "Huevos de gallina T3",
                    HasButcherBonus = true, FavFoodId = "T1_CARROT", FoodBonusCity = "Lymhurst",
                    Notes = "+10% huevos al criar, +10% carne al carnicear. Zanahoria (favorita) tiene bonus en Lymhurst." },
        ],
        ["Martlock"] =
        [
            new() { ItemId = "T8_COW",         NameEs = "Vaca",               Tier = 8, Type = FarmItemType.Animal,
                    ProductItemId = "T8_COWSMILK", ProductNameEs = "Leche de vaca T8",
                    HasButcherBonus = true, FavFoodId = "T7_CORN", FoodBonusCity = "Bridgewatch",
                    Notes = "Leche más valiosa — ingrediente comidas top-tier. Maíz (favorita) en Bridgewatch." },
            new() { ItemId = "T6_FOXGLOVE",    NameEs = "Dedalera esquiva",   Tier = 6, Type = FarmItemType.Hierba,  Notes = "Pociones de curación T6 — demanda constante PvP y raids" },
            new() { ItemId = "T6_POTATO",      NameEs = "Patata",             Tier = 6, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media, Notes = "Comidas T6, pociones de energía" },
            new() { ItemId = "T3_WHEAT",       NameEs = "Trigo",              Tier = 3, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media, Notes = "Pan, cerveza, comidas básicas" },
        ],
        ["Caerleon"] =
        [
            new() { ItemId = "T3_BRIGHTBLOOM_STALK",  NameEs = "Consuelda hojabrillante", Tier = 3, Type = FarmItemType.Hierba, Notes = "Única ciudad real con este bonus — ingrediente Poción de Veneno Menor" },
            new() { ItemId = "T5_TEASEL_ROOT",        NameEs = "Cardo borriqueño",        Tier = 5, Type = FarmItemType.Hierba, Notes = "Alternativa a Bridgewatch para Pociones de Resistencia" },
            new() { ItemId = "T7_FIRETOUCHED_MULLEIN",NameEs = "Gordolobo de fuego",      Tier = 7, Type = FarmItemType.Hierba, Notes = "Ingrediente Poción de Veneno Mayor" },
        ],
        ["Brecilien"] =
        [
            new() { ItemId = "T8_PUMPKIN", NameEs = "Calabaza",  Tier = 8, Type = FarmItemType.Cultivo, Notes = "Máxima rentabilidad" },
            new() { ItemId = "T7_CORN",    NameEs = "Maíz",      Tier = 7, Type = FarmItemType.Cultivo, Notes = "Alta demanda constante" },
            new() { ItemId = "T6_POTATO",  NameEs = "Patata",    Tier = 6, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
            new() { ItemId = "T5_CABBAGE", NameEs = "Repollo",   Tier = 5, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
            new() { ItemId = "T4_TURNIP",  NameEs = "Nabo",      Tier = 4, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
            new() { ItemId = "T3_WHEAT",   NameEs = "Trigo",     Tier = 3, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
            new() { ItemId = "T2_BEAN",    NameEs = "Alubia",    Tier = 2, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
            new() { ItemId = "T1_CARROT",  NameEs = "Zanahoria", Tier = 1, Type = FarmItemType.Cultivo, Priority = FarmPriority.Media },
        ],
    };

    private static readonly Dictionary<string, string[]> CityAvoid = new()
    {
        ["Thetford"]      = ["Bardana almenada (bonus en Lymhurst)", "Consuelda (bonus en Caerleon)", "Animales de leche o huevos (sin bonus aquí)", "Dedalera, Yarrow, Cardo (otras ciudades)"],
        ["Lymhurst"]      = ["Gordolobo de fuego (bonus en Thetford)", "Consuelda (bonus en Caerleon)", "Vaca, Cerdo, Oveja, Cabra (sin bonus de leche/cría)", "Maíz, Patata, Trigo (sin bonus aquí)"],
        ["Bridgewatch"]   = ["Vaca T8 (leche en Martlock)", "Gordolobo, Yarrow, Dedalera (otras ciudades)", "Calabaza, Patata (sin bonus aquí)", "Trigo (bonus en Martlock)"],
        ["Fort Sterling"] = ["Gordolobo de fuego (Thetford/Caerleon)", "Cerdo, Vaca, Ganso, Cabra (sin bonus de cría)", "Calabaza, Maíz, Patata (sin bonus)", "Dedalera (bonus en Martlock)"],
        ["Martlock"]      = ["Maíz (bonus en Bridgewatch — importar para vacas)", "Calabaza (bonus en Lymhurst)", "Cerdo, Ganso, Cabra, Oveja, Gallina (sin bonus)", "Gordolobo, Yarrow, Cardo (otras ciudades)"],
        ["Caerleon"]      = ["Cualquier cultivo (sin farming bonus)", "Cualquier animal (sin farming bonus)", "Dedalera, Yarrow, Bardana (otras ciudades)"],
        ["Brecilien"]     = ["Jardines de hierbas (sin bonus en ninguna hierba)", "Pasturas y corrales (sin bonus de animales)", "Gordolobo, Yarrow, Dedalera, Bardana (otras ciudades)"],
    };

    private static readonly Dictionary<string, string> CraftingBonuses = new()
    {
        ["Caerleon"]  = "Comida +15% · Herramientas +15% · Equipo recolección +15%",
        ["Brecilien"] = "Alquimia (Pociones) +15%",
    };

    // ── Recipe data (loaded from JSON) ────────────────────────────────────────

    public List<FarmingRecipe> Recipes { get; private set; } = [];

    public void LoadRecipes()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "farming_recipes.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Recipes = JsonSerializer.Deserialize<List<FarmingRecipe>>(json, opts) ?? [];
        }
        catch { }
    }

    // ── Public accessors ──────────────────────────────────────────────────────

    public List<FarmingBonus> GetBonuses(string city) =>
        CityBonuses.TryGetValue(city, out var list) ? list : [];

    public string[] GetAvoid(string city) =>
        CityAvoid.TryGetValue(city, out var arr) ? arr : [];

    public string? GetCraftingBonus(string city) =>
        CraftingBonuses.TryGetValue(city, out var s) ? s : null;

    public IEnumerable<string> GetAllItemIds(IEnumerable<IslandConfig> islands)
    {
        var ids = new HashSet<string>();
        foreach (var island in islands)
        {
            foreach (var b in GetBonuses(island.City))
            {
                ids.Add(b.ItemId);
                if (b.ProductItemId != null) ids.Add(b.ProductItemId);
                if (b.FavFoodId    != null) ids.Add(b.FavFoodId);
            }
        }
        foreach (var r in Recipes)
        {
            ids.Add(r.OutputId);
            foreach (var ing in r.Ingredients) ids.Add(ing.ItemId);
        }
        return ids;
    }

    // ── Synergy detection ─────────────────────────────────────────────────────

    public List<FarmingSynergy> DetectSynergies(IEnumerable<IslandConfig> islands)
    {
        var islandList = islands.ToList();
        var result     = new List<FarmingSynergy>();
        var cities     = islandList.Select(i => i.City).ToHashSet();

        // 1. Animal food synergies
        foreach (var islandB in islandList)
        {
            foreach (var animal in GetBonuses(islandB.City).Where(b => b.Type == FarmItemType.Animal && b.FavFoodId != null))
            {
                // Check if food bonus city is a DIFFERENT island the user has
                if (animal.FoodBonusCity == null || animal.FoodBonusCity == islandB.City) continue;
                if (!cities.Contains(animal.FoodBonusCity)) continue;

                var foodName = GetBonuses(animal.FoodBonusCity)
                    .FirstOrDefault(b => b.ItemId == animal.FavFoodId)?.NameEs ?? animal.FavFoodId;

                result.Add(new FarmingSynergy
                {
                    Type   = SynergyType.AlimentoAnimal,
                    Title  = $"{animal.FoodBonusCity} → {islandB.City}: comida para {animal.NameEs}",
                    Description =
                        $"Tu isla en {animal.FoodBonusCity} produce {foodName} con +10% bonus. " +
                        $"Es la comida favorita del {animal.NameEs} en {islandB.City}, " +
                        $"reduciendo el consumo de comida a la mitad (18→9 ud.). " +
                        $"Ahorrás ~50% del costo de alimentación por ciclo.",
                    Cities         = [animal.FoodBonusCity, islandB.City],
                    IsFullyCovered = true,
                });
            }
        }

        // 2. Recipe ingredient synergies
        foreach (var recipe in Recipes)
        {
            // Deduplicate ingredients by bonusCity (Poison Mayor has duplicate entry)
            var uniqueIngredients = recipe.Ingredients
                .GroupBy(i => i.BonusCity)
                .Select(g => g.First())
                .Where(i => i.Qty > 0)
                .ToList();

            var covered = uniqueIngredients.Where(i => cities.Contains(i.BonusCity)).ToList();
            if (covered.Count == 0) continue;

            var missing = uniqueIngredients
                .Where(i => !cities.Contains(i.BonusCity))
                .Select(i => $"{i.NameEs} (bonus en {i.BonusCity})")
                .ToList();

            var involvedCities = covered.Select(i => i.BonusCity).Distinct().ToList();
            if (recipe.CraftingBonusCity != null && cities.Contains(recipe.CraftingBonusCity))
                involvedCities.Add(recipe.CraftingBonusCity);

            var desc = covered.Count == uniqueIngredients.Count
                ? $"Tenés todas las islas para craftear {recipe.OutputName} con ingredientes propios con bonus."
                : $"Cubrís {covered.Count}/{uniqueIngredients.Count} ingredientes con bonus. Faltan: {string.Join(", ", missing)}.";

            if (recipe.CraftingBonusCity != null && cities.Contains(recipe.CraftingBonusCity))
                desc += $" Además tenés isla en {recipe.CraftingBonusCity} (+15% al craftear).";

            result.Add(new FarmingSynergy
            {
                Type             = SynergyType.Receta,
                Title            = recipe.OutputName,
                Description      = desc,
                Cities           = involvedCities,
                Recipe           = recipe,
                IsFullyCovered   = missing.Count == 0,
                MissingIngredients = missing,
            });
        }

        // 3. Crafting bonus synergies (standalone — not tied to a specific recipe)
        foreach (var island in islandList)
        {
            var bonus = GetCraftingBonus(island.City);
            if (bonus == null) continue;
            // Only add if there are OTHER islands whose items benefit from this bonus
            var hasOtherIslands = islandList.Any(i => i.City != island.City);
            if (!hasOtherIslands) continue;

            result.Add(new FarmingSynergy
            {
                Type        = SynergyType.BonusCrafteo,
                Title       = $"Bonus de crafteo: {island.City}",
                Description = $"Tu isla en {island.City} te da acceso a: {bonus}. " +
                              $"Llevá los materiales de tus otras islas a craftear aquí para mayor rendimiento.",
                Cities           = [island.City],
                IsFullyCovered   = true,
            });
        }

        return result;
    }

    public static readonly string[] AllCities =
        ["Thetford", "Lymhurst", "Bridgewatch", "Fort Sterling", "Martlock", "Caerleon", "Brecilien"];
}
