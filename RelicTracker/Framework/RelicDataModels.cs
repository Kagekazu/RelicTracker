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
}

public sealed class ExpansionMaterialRow
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("perUnit")]
    public double? PerUnit { get; set; }

    [JsonPropertyName("kettle")]
    public double? Kettle { get; set; }

    [JsonPropertyName("remaining")]
    public double? Remaining { get; set; }
}

public sealed class ExpansionSheet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("jobCount")]
    public int JobCount { get; set; }

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
    public double? PerUnit { get; set; }

    [JsonPropertyName("remaining")]
    public double? Remaining { get; set; }
}

public sealed class MaterialDisplayRow
{
    public required string ExpansionId { get; init; }
    public string? Step { get; init; }
    public required string Name { get; init; }
    public uint? ItemId { get; init; }
    public uint Needed { get; init; }
    public uint Owned { get; init; }
    public uint Shortfall => Needed > Owned ? Needed - Owned : 0;
    public bool IsCurrency { get; init; }
}
