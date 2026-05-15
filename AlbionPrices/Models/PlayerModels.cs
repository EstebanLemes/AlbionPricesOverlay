using System.Text.Json.Serialization;

namespace AlbionPrices.Models;

public class GameInfoSearchResult
{
    [JsonPropertyName("players")]
    public List<PlayerSearchEntry>? Players { get; set; }

    [JsonPropertyName("guilds")]
    public List<GuildSearchEntry>? Guilds { get; set; }
}

public class PlayerSearchEntry
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("GuildName")]
    public string? GuildName { get; set; }

    [JsonPropertyName("AllianceName")]
    public string? AllianceName { get; set; }

    [JsonPropertyName("AllianceTag")]
    public string? AllianceTag { get; set; }

    [JsonPropertyName("KillFame")]
    public long KillFame { get; set; }

    [JsonPropertyName("DeathFame")]
    public long DeathFame { get; set; }

    [JsonPropertyName("FameRatio")]
    public double? FameRatio { get; set; }
}

public class GuildSearchEntry
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("AllianceName")]
    public string? AllianceName { get; set; }

    [JsonPropertyName("AllianceTag")]
    public string? AllianceTag { get; set; }

    [JsonPropertyName("MemberCount")]
    public int MemberCount { get; set; }

    [JsonPropertyName("KillFame")]
    public long KillFame { get; set; }

    [JsonPropertyName("DeathFame")]
    public long DeathFame { get; set; }
}

public class PlayerInfo
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("AverageItemPower")]
    public double? AverageItemPower { get; set; }

    [JsonPropertyName("GuildId")]
    public string? GuildId { get; set; }

    [JsonPropertyName("GuildName")]
    public string? GuildName { get; set; }

    [JsonPropertyName("AllianceName")]
    public string? AllianceName { get; set; }

    [JsonPropertyName("AllianceTag")]
    public string? AllianceTag { get; set; }

    [JsonPropertyName("Avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("AvatarRing")]
    public string? AvatarRing { get; set; }

    [JsonPropertyName("KillFame")]
    public long KillFame { get; set; }

    [JsonPropertyName("DeathFame")]
    public long DeathFame { get; set; }

    [JsonPropertyName("FameRatio")]
    public double? FameRatio { get; set; }

    [JsonPropertyName("LifetimeStatistics")]
    public LifetimeStats? LifetimeStatistics { get; set; }
}

public class LifetimeStats
{
    [JsonPropertyName("PvE")]
    public FameCategory? PvE { get; set; }

    [JsonPropertyName("Gathering")]
    public GatheringStats? Gathering { get; set; }

    [JsonPropertyName("Crafting")]
    public FameCategory? Crafting { get; set; }
}

// Gathering has sub-categories; "All" holds the combined total.
public class GatheringStats
{
    [JsonPropertyName("All")]
    public FameCategory? All { get; set; }
}

public class FameCategory
{
    [JsonPropertyName("Total")]
    public long Total { get; set; }
}

public class KillEvent
{
    [JsonPropertyName("EventId")]
    public long EventId { get; set; }

    [JsonPropertyName("TimeStamp")]
    public DateTime? TimeStamp { get; set; }

    [JsonPropertyName("Killer")]
    public KillParticipant? Killer { get; set; }

    [JsonPropertyName("Victim")]
    public KillParticipant? Victim { get; set; }

    [JsonPropertyName("Participants")]
    public List<KillParticipant>? Participants { get; set; }

    [JsonPropertyName("TotalVictimKillFame")]
    public long TotalVictimKillFame { get; set; }
}

public class KillParticipant
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("GuildName")]
    public string? GuildName { get; set; }

    [JsonPropertyName("Equipment")]
    public KillEquipment? Equipment { get; set; }

    [JsonPropertyName("Inventory")]
    public List<EquipmentItem?>? Inventory { get; set; }

    [JsonPropertyName("AverageItemPower")]
    public double? AverageItemPower { get; set; }
}

public class KillEquipment
{
    [JsonPropertyName("MainHand")] public EquipmentItem? MainHand { get; set; }
    [JsonPropertyName("OffHand")]  public EquipmentItem? OffHand  { get; set; }
    [JsonPropertyName("Head")]     public EquipmentItem? Head     { get; set; }
    [JsonPropertyName("Armor")]    public EquipmentItem? Armor    { get; set; }
    [JsonPropertyName("Shoes")]    public EquipmentItem? Shoes    { get; set; }
    [JsonPropertyName("Bag")]      public EquipmentItem? Bag      { get; set; }
    [JsonPropertyName("Cape")]     public EquipmentItem? Cape     { get; set; }
    [JsonPropertyName("Mount")]    public EquipmentItem? Mount    { get; set; }
    [JsonPropertyName("Potion")]   public EquipmentItem? Potion   { get; set; }
    [JsonPropertyName("Food")]     public EquipmentItem? Food     { get; set; }
}

public class EquipmentItem
{
    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Quality")]
    public int Quality { get; set; }

    [JsonPropertyName("Count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("Dropped")]
    public bool? Dropped { get; set; }

    public double? EstimatedMarketValue { get; set; }
}
