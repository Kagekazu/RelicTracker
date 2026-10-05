using System.Globalization;

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

public sealed class ExpansionMaterialRow
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("materialIds")]
    public List<uint> MaterialIds { get; set; } = [];

    [JsonPropertyName("jobs")]
    public List<bool?> Jobs { get; set; } = [];

    [JsonPropertyName("perUnit")]
    [JsonConverter(typeof(FlexibleDoubleJsonConverter))]
    public double? PerUnit { get; set; }

    [JsonIgnore]
    public uint PerUnitCount => (uint)Math.Max(0, Math.Round(PerUnit ?? 0));

    [JsonPropertyName("purchase")]
    public MaterialPurchase? Purchase { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("craftOf")]
    public string? CraftOf { get; set; }

    /// <summary>How the material is obtained: craft, gather, fish, buy, or content.</summary>
    [JsonPropertyName("obtain")]
    public string? Obtain { get; set; }

    /// <summary>Currency for <c>buy</c>, token for <c>content</c> trades, or a zone hint.</summary>
    [JsonPropertyName("obtainVia")]
    public string? ObtainVia { get; set; }

    /// <summary>One-time step: needed once, before the first weapon of the step - not per job.</summary>
    [JsonPropertyName("oneTime")]
    public bool OneTime { get; set; }

    /// <summary>Has a recipe although it's usually obtained another way (bought Anima crafts).</summary>
    [JsonPropertyName("craftable")]
    public bool Craftable { get; set; }

    [JsonIgnore]
    public string ObtainLabel => MaterialObtain.Label(Obtain, ObtainVia);
}

public static class MaterialObtain
{
    public static string Label(string? obtain, string? via)
    {
        var hasVia = !string.IsNullOrWhiteSpace(via);
        return obtain switch
        {
            "buy" => hasVia ? $"Buy · {via}" : "Buy",
            "craft" => "Craft",
            "gather" => hasVia ? $"Gather · {via}" : "Gather",
            "fish" => "Fish",
            "content" => hasVia ? $"Trade {via}" : "Content drop",
            _ => string.Empty,
        };
    }
}

public sealed class MaterialPurchase
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("unit")]
    public int Unit { get; set; }
}

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

    [JsonPropertyName("currencyIds")]
    public List<uint> CurrencyIds { get; set; } = [];

    [JsonPropertyName("perPiece")]
    public int PerPiece { get; set; }

    [JsonPropertyName("setTotal")]
    public int SetTotal { get; set; }

    [JsonPropertyName("allTotal")]
    public int AllTotal { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

internal sealed class FlexibleDoubleJsonConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => TryParse(reader.GetString()),
            var _ => null
        };

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }

    private static double? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim().Replace(",", string.Empty);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

public sealed class RelicDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleDoubleJsonConverter() }
    };

    public RelicManifest Manifest { get; private set; } = new();

    public Dictionary<string, ExpansionSheet> Expansions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> MaterialSources { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<uint>> MaterialIdsByName { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<ArmorCostRow>> ArmorCosts { get; private set; } = new(StringComparer.Ordinal);

    public void Load()
    {
        Manifest = BundledData.ReadJson<RelicManifest>("manifest.json", JsonOptions) ?? new RelicManifest();
        MaterialSources = BundledData.ReadJson<Dictionary<string, string>>("material_sources.json", JsonOptions)
                          ?? new(StringComparer.OrdinalIgnoreCase);
        ArmorCosts = BundledData.ReadJson<Dictionary<string, List<ArmorCostRow>>>("armor_costs.json", JsonOptions)
                     ?? new(StringComparer.Ordinal);
        MergeExtraMaterials();
        BuildMaterialIdIndex();
        Svc.Log.Information(
            "[RelicTracker] Loaded relic materials for {ExpansionCount} expansions.",
            Expansions.Count);
    }

    private void MergeExtraMaterials()
    {
        var extra = BundledData.ReadJson<Dictionary<string, List<ExpansionMaterialRow>>>(
            "tool_extra_materials.json",
            JsonOptions);
        if (extra is null)
        {
            return;
        }

        foreach ((var expansionId, var materials) in extra)
        {
            if (!Expansions.TryGetValue(expansionId, out var sheet))
            {
                sheet = new() { Id = expansionId };
                Expansions[expansionId] = sheet;
            }

            sheet.Materials.Clear();
            sheet.Materials.AddRange(materials);
        }
    }

    private void BuildMaterialIdIndex()
    {
        Dictionary<string, List<uint>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Expansions.Values)
        {
            foreach (var row in sheet.Materials)
            {
                var name = row.Material?.Trim();
                if (string.IsNullOrWhiteSpace(name) || row.MaterialIds.Count == 0 || byName.ContainsKey(name))
                {
                    continue;
                }

                byName[name] = [.. row.MaterialIds];
            }
        }

        MaterialIdsByName = byName.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<uint>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
