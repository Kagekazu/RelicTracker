namespace RelicTracker.Framework;

public sealed class FfxivCollectSnapshot
{
    public static FfxivCollectSnapshot Empty { get; } = new();

    public ulong CharacterId { get; init; }
    public List<FfxivCollectRelic> Owned { get; init; } = [];
    public List<FfxivCollectRelic> Missing { get; init; } = [];
}

public sealed class FfxivCollectRelic
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("achievement_id")]
    public int? AchievementId { get; set; }

    [JsonPropertyName("icon")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("type")]
    public FfxivCollectRelicType? Type { get; set; }

    [JsonIgnore]
    public string Category => Type?.Category ?? "unknown";

    [JsonIgnore]
    public string TypeName => Type?.Name ?? "Unknown";
}

public sealed class FfxivCollectRelicType
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("jobs")]
    public int? Jobs { get; set; }

    [JsonPropertyName("order")]
    public int? Order { get; set; }

    [JsonPropertyName("expansion")]
    public int? Expansion { get; set; }
}

internal sealed class FfxivCollectIndexResponse
{
    [JsonPropertyName("results")]
    public List<FfxivCollectRelic>? Results { get; set; }
}

internal sealed class FfxivCollectApiError
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class FfxivCollectException(string message) : Exception(message)
{
}
