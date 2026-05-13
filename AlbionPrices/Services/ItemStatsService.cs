using System.Text.RegularExpressions;
using System.Net;
using AlbionPrices.Models;

namespace AlbionPrices.Services;

public static class ItemStatsService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "AlbionPrices/1.0" } },
    };

    private static readonly Regex TieredItemRegex =
        new(@"^T(?<tier>\d)_(?<base>.+?)(?:@(?<enchant>\d))?$", RegexOptions.Compiled);

    public static ItemStatsSummary BuildSummary(string itemId, string itemName, int quality)
    {
        var match = TieredItemRegex.Match(itemId);
        var tier = match.Success ? int.Parse(match.Groups["tier"].Value) : 0;
        var enchant = match.Success && match.Groups["enchant"].Success
            ? int.Parse(match.Groups["enchant"].Value)
            : 0;
        var familyId = match.Success ? match.Groups["base"].Value : itemId;
        var category = GetCategory(familyId);
        var slot = GetSlot(familyId);
        var itemPower = EstimateItemPower(tier, enchant, quality, category);

        var summary = new ItemStatsSummary
        {
            ItemId = itemId,
            ItemName = itemName,
            Category = category,
            Slot = slot,
            TierLabel = tier > 0 ? $"T{tier}" : "N/A",
            EnchantLabel = enchant > 0 ? $".{enchant}" : ".0",
            QualityLabel = QualityLabel(quality),
            ItemPowerLabel = itemPower > 0 ? $"~{itemPower:N0} IP" : "N/A",
            VariantLabel = tier > 0 ? $"T{tier}.{enchant} / {QualityLabel(quality)}" : "Sin tier",
            FamilyId = familyId,
            ExternalUrl = BuildExternalUrl(itemName, category),
        };

        summary.Lines.Add(new("Tipo", category));
        summary.Lines.Add(new("Slot", slot));
        summary.Lines.Add(new("Variante", summary.VariantLabel));
        summary.Lines.Add(new("IP estimado", summary.ItemPowerLabel));
        summary.Lines.Add(new("Familia", familyId));
        AddInferredSections(summary, familyId, tier, enchant);

        return summary;
    }

    public static async Task<ItemStatsSummary?> EnrichFromAlbionDatabaseAsync(
        ItemStatsSummary current,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(current.ExternalUrl)) return null;

        try
        {
            var html = await Http.GetStringAsync(current.ExternalUrl, ct);
            if (string.IsNullOrWhiteSpace(html) || html.StartsWith("<!doctype html><html><head><title>404", StringComparison.OrdinalIgnoreCase))
                return null;

            var enriched = new ItemStatsSummary
            {
                ItemId = current.ItemId,
                ItemName = current.ItemName,
                Category = current.Category,
                Slot = current.Slot,
                TierLabel = current.TierLabel,
                EnchantLabel = current.EnchantLabel,
                QualityLabel = current.QualityLabel,
                ItemPowerLabel = current.ItemPowerLabel,
                VariantLabel = current.VariantLabel,
                FamilyId = current.FamilyId,
                ExternalUrl = current.ExternalUrl,
                Lines = [.. current.Lines],
                Sections = [.. current.Sections],
            };

            var stats = ParseStatsSection(html);
            if (stats.Count > 0)
            {
                enriched.Sections.RemoveAll(s => s.Title == "STATS DE FICHA");
                enriched.Sections.Insert(0, new ItemStatSection
                {
                    Title = "STATS DE FICHA",
                    Lines = stats,
                });
            }

            var spells = ParseSpellSection(html);
            if (spells.Count > 0)
            {
                enriched.Sections.RemoveAll(s => s.Title == "HABILIDADES Y PASIVAS");
                enriched.Sections.Add(new ItemStatSection
                {
                    Title = "HABILIDADES Y PASIVAS",
                    Lines = spells,
                });
            }

            var craft = ParseCraftingSection(html);
            if (craft.Count > 0)
            {
                enriched.Sections.RemoveAll(s => s.Title == "CRAFTING");
                enriched.Sections.Add(new ItemStatSection
                {
                    Title = "CRAFTING",
                    Lines = craft,
                });
            }

            return enriched;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCategory(string familyId)
    {
        var id = familyId.ToUpperInvariant();
        if (id.StartsWith("MAIN_") || id.StartsWith("2H_") || id.StartsWith("OFF_")) return "Arma";
        if (id.StartsWith("HEAD_") || id.StartsWith("ARMOR_") || id.StartsWith("SHOES_")) return "Armadura";
        if (id.StartsWith("BAG") || id.StartsWith("CAPE")) return "Accesorio";
        if (id.Contains("_MOUNT") || id.StartsWith("MOUNT_")) return "Montura";
        if (id.Contains("_POTION")) return "Pocion";
        if (id.Contains("_MEAL") || id.Contains("FISH")) return "Comida";
        if (IsResource(id)) return "Recurso";
        if (id.Contains("JOURNAL")) return "Diario";
        return "Item";
    }

    private static string GetSlot(string familyId)
    {
        var id = familyId.ToUpperInvariant();
        if (id.StartsWith("HEAD_")) return "Cabeza";
        if (id.StartsWith("ARMOR_")) return "Pecho";
        if (id.StartsWith("SHOES_")) return "Botas";
        if (id.StartsWith("MAIN_")) return "Mano principal";
        if (id.StartsWith("2H_")) return "Dos manos";
        if (id.StartsWith("OFF_")) return "Mano secundaria";
        if (id.StartsWith("BAG")) return "Bolsa";
        if (id.StartsWith("CAPE")) return "Capa";
        if (id.Contains("_MOUNT") || id.StartsWith("MOUNT_")) return "Montura";
        if (IsResource(id)) return "Material";
        return "General";
    }

    private static bool IsResource(string id) =>
        id is "ORE" or "FIBER" or "WOOD" or "HIDE" or "ROCK" or
            "METALBAR" or "CLOTH" or "PLANKS" or "LEATHER" or "STONEBLOCK" or
            "RUNE" or "SOUL" or "RELIC" ||
        id.EndsWith("_ORE") || id.EndsWith("_FIBER") || id.EndsWith("_WOOD") ||
        id.EndsWith("_HIDE") || id.EndsWith("_ROCK") || id.EndsWith("_METALBAR") ||
        id.EndsWith("_CLOTH") || id.EndsWith("_PLANKS") || id.EndsWith("_LEATHER") ||
        id.EndsWith("_STONEBLOCK") || id.EndsWith("_RUNE") || id.EndsWith("_SOUL") ||
        id.EndsWith("_RELIC");

    private static int EstimateItemPower(int tier, int enchant, int quality, string category)
    {
        if (tier <= 0) return 0;

        var baseIp = category is "Arma" or "Armadura" or "Accesorio"
            ? 700 + (tier - 4) * 100
            : 0;
        if (baseIp == 0) return 0;

        var qualityBonus = quality switch
        {
            2 => 10,
            3 => 20,
            4 => 50,
            5 => 100,
            _ => 0,
        };

        return baseIp + enchant * 100 + qualityBonus;
    }

    private static string QualityLabel(int quality) => quality switch
    {
        2 => "Bueno",
        3 => "Sobresaliente",
        4 => "Excelente",
        5 => "Obra maestra",
        _ => "Normal",
    };

    private static string BuildExternalUrl(string itemName, string category)
    {
        var slug = Slugify(itemName);
        if (string.IsNullOrWhiteSpace(slug)) return "https://www.albiondatabase.com/";

        var section = category switch
        {
            "Arma" => "weapons",
            "Armadura" or "Accesorio" => "armor",
            "Montura" => "mounts",
            _ => "items",
        };
        return $"https://www.albiondatabase.com/{section}/{slug}";
    }

    private static string Slugify(string text)
    {
        var formD = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is '\'') continue;
            else if (c is ' ' or '-' or '.') sb.Append('-');
        }

        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    private static void AddInferredSections(ItemStatsSummary summary, string familyId, int tier, int enchant)
    {
        var craftLines = InferCrafting(familyId, tier, enchant);
        if (craftLines.Count > 0)
        {
            summary.Sections.Add(new ItemStatSection
            {
                Title = "CRAFTING",
                Lines = craftLines,
            });
        }

        if (summary.Category is "Arma" or "Armadura")
        {
            summary.Sections.Add(new ItemStatSection
            {
                Title = "HABILIDADES Y PASIVAS",
                Lines =
                [
                    new("Fuente", "Se cargan desde Ficha si AlbionDatabase responde"),
                    new("Local", "La app muestra precios/stats; spell data completa viene de la ficha"),
                ],
            });
        }
    }

    private static List<ItemStatLine> InferCrafting(string familyId, int tier, int enchant)
    {
        if (tier <= 0) return [];

        var id = familyId.ToUpperInvariant();
        var material = id switch
        {
            _ when id.StartsWith("HEAD_") || id.StartsWith("ARMOR_") || id.StartsWith("SHOES_") => "material principal segun rama",
            _ when id.StartsWith("MAIN_") || id.StartsWith("2H_") || id.StartsWith("OFF_") => "metal/madera/cuero/tela segun arma",
            _ when id.StartsWith("BAG") => "cuero",
            _ when id.StartsWith("CAPE") => "tela",
            _ => "",
        };
        if (material == "") return [];

        var lines = new List<ItemStatLine>
        {
            new("Base", $"T{tier} con {material}"),
        };
        if (enchant > 0)
        {
            var artifact = enchant switch
            {
                1 => "runas",
                2 => "almas",
                3 => "reliquias",
                4 => "material prismatico",
                _ => "material de encantamiento",
            };
            lines.Add(new("Encant.", $".{enchant} usa {artifact} T{tier}"));
        }
        lines.Add(new("Detalle", "La receta exacta se completa desde Ficha"));
        return lines;
    }

    private static List<ItemStatLine> ParseStatsSection(string html)
    {
        var section = ExtractSection(html, "Base Stats");
        if (string.IsNullOrEmpty(section)) section = ExtractSection(html, "Defense Stats");
        var tokens = TextTokens(section);
        if (tokens.Count == 0) return [];

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Item Power", "Ability Power", "Attack Damage", "Attack Speed", "Attack Range",
            "Durability", "Weight", "Max Quality", "Max HP", "HP Regen",
            "Focus Fire Pen.", "Mastery Modifier",
        };

        var lines = new List<ItemStatLine>();
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (!labels.Contains(tokens[i])) continue;
            lines.Add(new(tokens[i], tokens[i + 1]));
            if (lines.Count >= 8) break;
        }
        return lines;
    }

    private static List<ItemStatLine> ParseSpellSection(string html)
    {
        var section = ExtractSection(html, "Spells & Abilities");
        if (string.IsNullOrEmpty(section)) return [];

        var anchors = Regex.Matches(section, @"<a\b[^>]*href\s*=\s*[""'][^""']*/spells/[^""']*[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => CleanText(m.Groups[1].Value))
            .Where(t => t.Length > 0 && !t.StartsWith("Image:", StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        var lines = new List<ItemStatLine>();
        foreach (var ability in anchors)
        {
            var line = ParseAbilityLine(ability);
            if (!string.IsNullOrWhiteSpace(line.Value)) lines.Add(line);
        }

        if (lines.Count > 0) return lines;

        var tokens = TextTokens(section)
            .Where(t => t is not "P" and not "Passive" and not "Active")
            .Where(t => !t.Equals("Spells & Abilities", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var token in tokens)
        {
            var line = ParseAbilityLine(token);
            if (string.IsNullOrWhiteSpace(line.Value)) continue;
            if (lines.Any(existing => existing.Value == line.Value)) continue;
            lines.Add(line);
            if (lines.Count >= 10) break;
        }
        return lines;
    }

    private static ItemStatLine ParseAbilityLine(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return new("", "");

        var isActive = Regex.IsMatch(text, @"\b\d+(?:\.\d+)?\s*s\b", RegexOptions.IgnoreCase);
        var label = isActive ? "Habilidad" : "Pasiva";

        var active = Regex.Match(
            text,
            @"^(?<name>.+?)\s+(?<cooldown>\d+(?:\.\d+)?\s*s(?:\s+\d+(?:\.\d+)?\s*m)?)\s+(?<desc>.+)$",
            RegexOptions.IgnoreCase);
        if (active.Success)
        {
            var value = $"{active.Groups["name"].Value.Trim()} ({active.Groups["cooldown"].Value.Trim()})";
            var desc = active.Groups["desc"].Value.Trim();
            if (desc.Length > 0) value += $" - {desc}";
            return new(label, value);
        }

        var passive = Regex.Match(
            text,
            @"^(?<name>.+?)\s+(?<desc>(?:Increases|Reduces|Decreases|Improves|Every|After|When|While|Your|All)\b.+)$",
            RegexOptions.IgnoreCase);
        if (passive.Success)
        {
            return new(label, $"{passive.Groups["name"].Value.Trim()} - {passive.Groups["desc"].Value.Trim()}");
        }

        return new(label, ExtractAbilityName(text));
    }

    private static List<ItemStatLine> ParseCraftingSection(string html)
    {
        var section = ExtractSection(html, "Crafting Recipe");
        if (string.IsNullOrEmpty(section)) return [];

        var tokens = TextTokens(section)
            .Where(t => t is not "Resource" and not "Count" and not "Calculate crafting profit")
            .ToList();

        var lines = new List<ItemStatLine>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].StartsWith("Time:", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new("Tiempo/Foco", tokens[i]));
                continue;
            }

            if (i + 1 < tokens.Count && int.TryParse(tokens[i + 1].Replace(",", ""), out _))
            {
                lines.Add(new("Material", $"{tokens[i]} x {tokens[i + 1]}"));
                i++;
            }
        }

        var craftedAt = TextTokens(ExtractSection(html, "Crafted At")).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(craftedAt)) lines.Add(new("Banco", craftedAt));

        var locations = TextTokens(ExtractSection(html, "Crafting Locations"))
            .Where(t => !t.StartsWith("+", StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (locations.Count > 0) lines.Add(new("Bonus", string.Join(", ", locations)));

        return lines.Take(8).ToList();
    }

    private static string ExtractSection(string html, string heading)
    {
        var match = Regex.Matches(html, @"<h2\b[^>]*>.*?</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Cast<Match>()
            .FirstOrDefault(m => CleanText(m.Value).Equals(heading, StringComparison.OrdinalIgnoreCase));
        if (match == null) return "";
        var start = match.Index + match.Length;
        var next = Regex.Match(html[start..], @"<h2\b", RegexOptions.IgnoreCase);
        var end = next.Success ? start + next.Index : html.Length;
        return html[start..end];
    }

    private static List<string> TextTokens(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(p|div|li|tr|td|th|dt|dd|a|h3|span|section)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Regex.Replace(t, @"\s+", " ").Trim())
            .Where(t => t.Length > 0 && !t.StartsWith("Image:", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string CleanText(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string ExtractAbilityName(string text)
    {
        var match = Regex.Match(text, @"^(.+?)\s+(?:\d+(\.\d+)?\s*s|\d+(\.\d+)?\s*E|\d+(\.\d+)?\s*m|Every\b|Increases\b|Reduces\b)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }
}
