namespace RelicTracker.Framework;

public sealed class RelicManifest
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sheetVersion")]
    public string SheetVersion { get; set; } = string.Empty;

    [JsonPropertyName("patch")]
    public string Patch { get; set; } = string.Empty;

    [JsonPropertyName("expansions")]
    public List<string> Expansions { get; set; } = [];
}

/// <summary>One curated relic step material (from tool_extra_materials.json).</summary>
public sealed class ExpansionMaterialRow
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    /// <summary>Per-job flags (used only on DoH/DoL tool lines for the per-discipline split).</summary>
    [JsonPropertyName("jobs")]
    public List<bool?> Jobs { get; set; } = [];

    [JsonPropertyName("perUnit")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? PerUnit { get; set; }

    /// <summary>Optional vendor price for a purchasable material (e.g. GC seals), for display only.</summary>
    [JsonPropertyName("purchase")]
    public MaterialPurchase? Purchase { get; set; }

    /// <summary>When <c>craft</c>, this row is the collectable; when <c>precraft</c>, an intermediate crafted item.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Parent item name — collectable for precrafts/scrip, precraft for raw materials.</summary>
    [JsonPropertyName("craftOf")]
    public string? CraftOf { get; set; }
}

/// <summary>A material's vendor purchase price (currency + per-unit cost), shown as a tooltip.</summary>
public sealed class MaterialPurchase
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("unit")]
    public int Unit { get; set; }
}

/// <summary>An expansion's relic step materials, built entirely from the curated supplement.</summary>
public sealed class ExpansionSheet
{
    public string Id { get; set; } = string.Empty;

    public List<ExpansionMaterialRow> Materials { get; set; } = [];
}

public sealed class ArmorCostRow
{
    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("perPiece")]
    public int PerPiece { get; set; }

    [JsonPropertyName("setTotal")]
    public int SetTotal { get; set; }

    [JsonPropertyName("allTotal")]
    public int AllTotal { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
