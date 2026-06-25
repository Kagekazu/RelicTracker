namespace RelicTracker.Framework;

/// <summary>One material in the shopping list, scoped to the step that consumes it.</summary>
public sealed class ShoppingMaterialRow
{
    public required string Step { get; init; }
    public required int StepOrder { get; init; }
    public required string Material { get; init; }
    public required uint Need { get; init; }
    public required uint Owned { get; init; }
    public required bool Resolved { get; init; }

    /// <summary>Where the material is farmed (from material_sources.json); falls back to the step.</summary>
    public required string Source { get; init; }

    public uint Short => Need > Owned ? Need - Owned : 0;
}

/// <summary>
/// Builds the materials shopping list for an expansion: for each step, the per-weapon
/// material cost times the number of jobs that still need that step (from the FFXIV
/// Collect funnel). No cross-step merging, so every step's needs are shown explicitly.
/// </summary>
public static class ShoppingListBuilder
{
    // Wyn material-sheet step names that differ from the catalog step names.
    private static readonly Dictionary<string, string> WynToCatalogStep = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skybuilders"] = "Skybuilders'",
        ["Augmented Law's"] = "Augmented Law's Order",
        ["Kettle to the Mettle"] = "Zeta",
    };

    public static List<ShoppingMaterialRow> Build(
        string expansionId,
        ExpansionSheet sheet,
        IReadOnlyList<RelicLineStatus> statuses,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        IReadOnlyDictionary<string, string> sources)
    {
        var expansionStatuses = statuses
            .Where(status => string.Equals(status.Line.Expansion, expansionId, StringComparison.Ordinal))
            .OrderBy(status => status.Line.TypeOrder)
            .ToList();

        // Catalog step name -> (jobs still needing it, display order).
        var stepInfo = new Dictionary<string, (int JobsNeeding, int Order)>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var status in expansionStatuses)
        {
            for (var tier = 0; tier < status.Line.TierCount; tier++)
            {
                var jobsNeeding = Math.Max(0, status.Line.Jobs - status.ReachedPerStep[tier]);
                if (stepInfo.TryAdd(status.Line.StepName(tier), (jobsNeeding, order)))
                {
                    order++;
                }
            }
        }

        // Accumulate by (source, material): a material's source is fixed by its name, so this
        // merges the same material across weapon steps into one total per farm location. For
        // materials without a source, the source falls back to the step (keeps per-step rows).
        var accumulated = new Dictionary<(string Source, string Material), (uint Need, int Order)>();
        var keyOrder = new List<(string Source, string Material)>();

        foreach (var row in sheet.Materials)
        {
            var step = row.Step?.Trim();
            var material = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(step) || !MaterialFilters.IsTrackableMaterial(material))
            {
                continue;
            }

            var perUnit = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            if (perUnit == 0)
            {
                continue;
            }

            // Steps that don't map to a relic tier are non-relic material (e.g. the removed
            // Exquisite/Figmental glamour weapons, or Bozja gear) — skip them entirely.
            var catalogStep = WynToCatalogStep.TryGetValue(step!, out var mapped) ? mapped : step!;
            if (!stepInfo.TryGetValue(catalogStep, out var info) && !stepInfo.TryGetValue(step!, out info))
            {
                continue;
            }

            var need = perUnit * (uint)info.JobsNeeding;
            if (need == 0)
            {
                continue;
            }

            var source = sources.TryGetValue(material!, out var mappedSource) ? mappedSource : step!;
            var key = (source, material!);
            if (accumulated.TryGetValue(key, out var existing))
            {
                accumulated[key] = (existing.Need + need, Math.Min(existing.Order, info.Order));
            }
            else
            {
                accumulated[key] = (need, info.Order);
                keyOrder.Add(key);
            }
        }

        var result = new List<ShoppingMaterialRow>(keyOrder.Count);
        foreach (var key in keyOrder)
        {
            var (need, stepOrder) = accumulated[key];
            var itemIds = items.ResolveItemIds(key.Material);
            var owned = itemIds.Aggregate(0u, (total, itemId) => total + ownedLookup(itemId));
            result.Add(new ShoppingMaterialRow
            {
                Step = key.Source,
                StepOrder = stepOrder,
                Material = key.Material,
                Need = need,
                Owned = owned,
                Resolved = itemIds.Count > 0,
                Source = key.Source,
            });
        }

        return result;
    }
}
