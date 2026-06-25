using System.Text.Json.Serialization;

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

public sealed class MaterialReferenceRow
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("requirement")]
    public string? Requirement { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("expansion")]
    public string? Expansion { get; set; }

    [JsonIgnore]
    public bool IsSectionHeader =>
        string.Equals(Step, "Step", StringComparison.Ordinal)
        && string.Equals(Material, "Material", StringComparison.Ordinal);
}

public sealed class ExpansionMaterialRow
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("jobs")]
    public List<bool?> Jobs { get; set; } = [];

    [JsonPropertyName("perUnit")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? PerUnit { get; set; }

    [JsonPropertyName("kettle")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? Kettle { get; set; }

    [JsonPropertyName("remaining")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? Remaining { get; set; }
}

public sealed class ExpansionStepRow
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("jobs")]
    public List<bool?> Jobs { get; set; } = [];

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("perUnit")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? PerUnit { get; set; }

    [JsonPropertyName("remaining")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? Remaining { get; set; }
}

public sealed class ExpansionSheet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("jobCount")]
    public int JobCount { get; set; }

    [JsonPropertyName("jobNames")]
    public List<string> JobNames { get; set; } = [];

    [JsonPropertyName("steps")]
    public List<ExpansionStepRow> Steps { get; set; } = [];

    [JsonPropertyName("materials")]
    public List<ExpansionMaterialRow> Materials { get; set; } = [];

    [JsonPropertyName("currencies")]
    public List<ExpansionCurrencyRow> Currencies { get; set; } = [];
}

public sealed class ExpansionCurrencyRow
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("perUnit")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? PerUnit { get; set; }

    [JsonPropertyName("remaining")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? Remaining { get; set; }
}

/// <summary>
/// Curated per-expansion material supplement (tool_extra_materials.json): rows to fold into a
/// sheet, plus step names whose generated (Wyn) rows should be dropped first so accurate curated
/// rows replace them. Kept separate from the generated expansions.json so a re-extract can't wipe it.
/// </summary>
public sealed class ToolExtraMaterials
{
    [JsonPropertyName("replaceSteps")]
    public List<string> ReplaceSteps { get; set; } = [];

    [JsonPropertyName("materials")]
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

public sealed class MaterialDisplayRow
{
    public required string ExpansionId { get; init; }
    public required string Section { get; init; }
    public string? Step { get; init; }
    public string? Label { get; init; }
    public required string Name { get; init; }
    public uint? ItemId { get; init; }
    public IReadOnlyList<uint> ItemIds { get; init; } = [];
    public bool IsResolved => ItemIds.Count > 0;
    public uint Needed { get; init; }
    public uint Owned { get; init; }
    public uint Shortfall => Needed > Owned ? Needed - Owned : 0;
    public bool IsCurrency { get; init; }
    public bool IsCurrencyTracked { get; init; }
}
