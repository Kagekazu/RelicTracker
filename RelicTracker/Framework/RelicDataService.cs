using System.Text.Json;

namespace RelicTracker.Framework;

public sealed class RelicDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleDoubleJsonConverter() },
    };

    public RelicManifest Manifest { get; private set; } = new();
    public List<MaterialReferenceRow> MaterialReference { get; private set; } = [];
    public Dictionary<string, ExpansionSheet> Expansions { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> JobColumnsByExpansion { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>Material name -> where it is farmed, for grouping the shopping list by source.</summary>
    public Dictionary<string, string> MaterialSources { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Expansion id -> field-op relic armor currency costs (per piece).</summary>
    public Dictionary<string, List<ArmorCostRow>> ArmorCosts { get; private set; } = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public void Load()
    {
        var baseDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");
        Manifest = ReadJson<RelicManifest>(Path.Combine(baseDir, "manifest.json")) ?? new RelicManifest();
        Expansions = ReadJson<Dictionary<string, ExpansionSheet>>(Path.Combine(baseDir, "expansions.json"))
                     ?? new Dictionary<string, ExpansionSheet>(StringComparer.Ordinal);
        MaterialReference = ReadJson<List<MaterialReferenceRow>>(Path.Combine(baseDir, "materials.json")) ?? [];
        JobColumnsByExpansion = ReadJson<Dictionary<string, List<string>>>(Path.Combine(baseDir, "job_columns.json"))
                                ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        MaterialSources = ReadJson<Dictionary<string, string>>(Path.Combine(baseDir, "material_sources.json"))
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ArmorCosts = ReadJson<Dictionary<string, List<ArmorCostRow>>>(Path.Combine(baseDir, "armor_costs.json"))
                     ?? new Dictionary<string, List<ArmorCostRow>>(StringComparer.Ordinal);
        MergeExtraMaterials(Path.Combine(baseDir, "tool_extra_materials.json"));
        TagMaterialReferenceExpansions();
        IsLoaded = true;
        Svc.Log.Information(
            "[RelicTracker] Loaded data {Version} ({Patch}) with {ExpansionCount} expansions.",
            Manifest.SheetVersion,
            Manifest.Patch,
            Expansions.Count);
    }

    /// <summary>
    /// Folds curated step materials (e.g. the ARR Lucis tool currencies that Wyn's sheet lacks)
    /// into the loaded expansion sheets. Kept in its own file so a Wyn re-extract can't wipe them.
    /// </summary>
    private void MergeExtraMaterials(string path)
    {
        var extra = ReadJson<Dictionary<string, ToolExtraMaterials>>(path);
        if (extra is null)
        {
            return;
        }

        foreach (var (expansionId, entry) in extra)
        {
            if (!Expansions.TryGetValue(expansionId, out var sheet))
            {
                continue;
            }

            if (entry.ReplaceSteps.Count > 0)
            {
                var drop = new HashSet<string>(entry.ReplaceSteps, StringComparer.OrdinalIgnoreCase);
                sheet.Materials.RemoveAll(row => row.Step is not null && drop.Contains(row.Step.Trim()));
            }

            sheet.Materials.AddRange(entry.Materials);
        }
    }

    /// <summary>Per-step materials shopping list, scaled by jobs still needing each step (from FFXIV Collect).</summary>
    public List<ShoppingMaterialRow> GetShoppingMaterials(
        string expansionId,
        IReadOnlyList<RelicLineStatus> statuses,
        RelicOwnership ownership,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        string? lineFilter = null)
    {
        return Expansions.TryGetValue(expansionId, out var sheet)
            ? ShoppingListBuilder.Build(expansionId, sheet, statuses, ownership, items, ownedLookup, MaterialSources, lineFilter)
            : [];
    }

    /// <summary>Wallet currencies still needed for an expansion (Poetics, seals, scrips, …).</summary>
    public IEnumerable<MaterialDisplayRow> GetExpansionCurrencies(
        string expansionId,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        RelicProgressTracker progress)
    {
        if (!Expansions.TryGetValue(expansionId, out var sheet))
        {
            yield break;
        }

        var currencyTotals = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Currencies)
        {
            var name = row.Name?.Trim();
            if (!MaterialFilters.IsTrackableCurrency(name))
            {
                continue;
            }

            var perUnit = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            currencyTotals.TryGetValue(name!, out var existingTotal);
            currencyTotals[name!] = existingTotal + perUnit;
        }

        foreach (var (name, totalPerUnit) in currencyTotals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new MaterialDisplayRow
            {
                ExpansionId = expansionId,
                Section = "Currencies",
                Step = null,
                Label = null,
                Name = name,
                ItemId = null,
                ItemIds = [],
                Needed = CurrencyBalances.CalculateNeeded(expansionId, totalPerUnit, sheet, progress),
                Owned = CurrencyBalances.GetOwned(name, items, ownedLookup),
                IsCurrency = true,
                IsCurrencyTracked = CurrencyBalances.IsTrackable(name),
            };
        }
    }

    public IEnumerable<MaterialReferenceRow> GetMaterialReference(string expansionId)
    {
        return MaterialReference.Where(row =>
            !row.IsSectionHeader
            && string.Equals(row.Expansion, expansionId, StringComparison.Ordinal)
            && (!string.IsNullOrWhiteSpace(row.Material) || !string.IsNullOrWhiteSpace(row.Location)));
    }

    private void TagMaterialReferenceExpansions()
    {
        if (MaterialReference.Count == 0)
        {
            return;
        }

        if (MaterialReference.Any(row => !string.IsNullOrEmpty(row.Expansion)))
        {
            return;
        }

        var dohSteps = Expansions.TryGetValue("DoHDoL", out var dohSheet)
            ? dohSheet.Materials
                .Select(row => row.Step)
                .Where(step => !string.IsNullOrEmpty(step))
                .Select(step => step!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var headerIndices = MaterialReference
            .Select((row, index) => (row, index))
            .Where(pair => pair.row.IsSectionHeader)
            .Select(pair => pair.index)
            .ToList();

        var sectionBounds = new List<int> { 0 };
        sectionBounds.AddRange(headerIndices.Select(index => index + 1));
        sectionBounds.Add(MaterialReference.Count);
        sectionBounds = sectionBounds.Distinct().OrderBy(index => index).ToList();

        var sectionExpansions = new[] { "ARR", "HW", "SB", "ShB", "EW", "DT", "DoHDoL" };
        for (var sectionIndex = 0; sectionIndex < sectionExpansions.Length && sectionIndex + 1 < sectionBounds.Count; sectionIndex++)
        {
            var start = sectionBounds[sectionIndex];
            var end = sectionBounds[sectionIndex + 1];
            var sectionExpansion = sectionExpansions[sectionIndex];

            for (var rowIndex = start; rowIndex < end; rowIndex++)
            {
                var row = MaterialReference[rowIndex];
                if (row.IsSectionHeader)
                {
                    continue;
                }

                var step = row.Step;
                row.Expansion = sectionExpansion == "DT"
                    && !string.IsNullOrEmpty(step)
                    && dohSteps.Contains(step)
                    ? "DoHDoL"
                    : sectionExpansion;
            }
        }
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing data file: {Path}", path);
            return default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to read {Path}", path);
            return default;
        }
    }
}