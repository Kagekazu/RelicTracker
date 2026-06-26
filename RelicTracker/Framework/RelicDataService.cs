namespace RelicTracker.Framework;

public sealed class RelicDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleDoubleJsonConverter() }
    };

    public RelicManifest Manifest { get; private set; } = new();

    /// <summary>Expansion id -> relic step materials (built entirely from the curated supplement).</summary>
    public Dictionary<string, ExpansionSheet> Expansions { get; } = new(StringComparer.Ordinal);

    /// <summary>Material name -> where it is farmed, for grouping the shopping list by source.</summary>
    public Dictionary<string, string> MaterialSources { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Expansion id -> field-op relic armor currency costs (per piece).</summary>
    public Dictionary<string, List<ArmorCostRow>> ArmorCosts { get; private set; } = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public void Load()
    {
        string baseDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");
        Manifest = ReadJson<RelicManifest>(Path.Combine(baseDir, "manifest.json")) ?? new RelicManifest();
        MaterialSources = ReadJson<Dictionary<string, string>>(Path.Combine(baseDir, "material_sources.json"))
                          ?? new(StringComparer.OrdinalIgnoreCase);
        ArmorCosts = ReadJson<Dictionary<string, List<ArmorCostRow>>>(Path.Combine(baseDir, "armor_costs.json"))
                     ?? new(StringComparer.Ordinal);
        MergeExtraMaterials(Path.Combine(baseDir, "tool_extra_materials.json"));
        IsLoaded = true;
        Svc.Log.Information(
            "[RelicTracker] Loaded relic materials for {ExpansionCount} expansions.",
            Expansions.Count);
    }

    /// <summary>
    ///     Loads the curated relic materials — the single source of truth for every relic line's step
    ///     materials. Creates the expansion sheet if needed and replaces any materials wholesale.
    /// </summary>
    private void MergeExtraMaterials(string path)
    {
        Dictionary<string, List<ExpansionMaterialRow>>? extra = ReadJson<Dictionary<string, List<ExpansionMaterialRow>>>(path);
        if (extra is null)
        {
            return;
        }

        foreach ((string expansionId, List<ExpansionMaterialRow> materials) in extra)
        {
            if (!Expansions.TryGetValue(expansionId, out ExpansionSheet? sheet))
            {
                sheet = new()
                { Id = expansionId };
                Expansions[expansionId] = sheet;
            }

            sheet.Materials.Clear();
            sheet.Materials.AddRange(materials);
        }
    }

    /// <summary>Per-step materials shopping list, scaled by the jobs still needing each step.</summary>
    public List<ShoppingMaterialRow> GetShoppingMaterials(
        string expansionId,
        IReadOnlyList<RelicLineStatus> statuses,
        RelicOwnership ownership,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        string? lineFilter = null) =>
        Expansions.TryGetValue(expansionId, out ExpansionSheet? sheet)
            ? ShoppingListBuilder.Build(expansionId, sheet, statuses, ownership, items, ownedLookup, MaterialSources, lineFilter)
            : [];

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing data file: {Path}", path);
            return default;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to read {Path}", path);
            return default;
        }
    }
}
