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
    public double FameRatio { get; set; }
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
    public double FameRatio { get; set; }

    [JsonPropertyName("LifetimeStatistics")]
    public LifetimeStats? LifetimeStatistics { get; set; }
}

public class LifetimeStats
{
    [JsonPropertyName("PvE")]
    public FameCategory? PvE { get; set; }

    [JsonPropertyName("Gathering")]
    public FameCategory? Gathering { get; set; }

    [JsonPropertyName("Crafting")]
    public FameCategory? Crafting { get; set; }
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
}
