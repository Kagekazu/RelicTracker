namespace RelicTracker.Framework;

public sealed class ShoppingMaterialRow
{
    public required string Step { get; init; }

    public required int StepOrder { get; init; }
    public required string Material { get; init; }

    public required string DisplayMaterial { get; init; }

    public required uint Need { get; init; }

    public required uint OwnedInventory { get; init; }

    public required uint OwnedQuestCredit { get; init; }

    public uint Owned => OwnedInventory + OwnedQuestCredit;
    public required bool Resolved { get; init; }

    public MaterialPurchase? Purchase { get; init; }

    public uint Short => Need > Owned ? Need - Owned : 0;
}

public sealed class ShoppingQuestRewardRow
{
    public required string CatalogStep { get; init; }
    public required string Material { get; init; }
    public required string DisplayMaterial { get; init; }
    public required uint Owned { get; init; }
    public required bool Resolved { get; init; }
}

public static class ShoppingListBuilder
{
    private const int FisherSlot = 10;
    private static readonly Dictionary<string, string> MaterialStepAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skybuilders"] = "Skybuilders'",
        ["Augmented Law's"] = "Augmented Law's Order",
        ["Kettle to the Mettle"] = "Zeta"
    };

    public static List<ShoppingMaterialRow> Build(
        string expansionId,
        ExpansionSheet sheet,
        IReadOnlyList<RelicLineStatus> statuses,
        RelicOwnership ownership,
        Func<uint, uint> ownedLookup,
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, IReadOnlyList<uint>> materialIdsByName,
        string? lineFilter = null)
    {
        var isTools = string.Equals(expansionId, "DoHDoL", StringComparison.Ordinal);

        Dictionary<string, (RelicLineStatus Status, int Tier, int Order)> stepInfo = new(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var status in statuses
            .Where(s => string.Equals(s.Line.Expansion, expansionId, StringComparison.Ordinal))
            .Where(s => string.IsNullOrEmpty(lineFilter) || string.Equals(s.Line.CollectType, lineFilter, StringComparison.Ordinal))
            .OrderBy(s => s.Line.TypeOrder))
        {
            for (var tier = 0; tier < status.TierCount; tier++)
            {
                if (stepInfo.TryAdd(status.Line.StepName(tier), (status, tier, order)))
                {
                    order++;
                }
            }
        }

        QuestRewardIndex questRewards = BuildQuestRewardIndex(sheet);

        Dictionary<(string Source, string Material), (uint Need, int Order, int JobsNeeding, string CatalogStep)> accumulated = [];
        List<(string Source, string Material)> keyOrder = [];
        HashSet<string> seenStepMaterial = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MaterialPurchase> purchaseByMaterial = new(StringComparer.OrdinalIgnoreCase);

        // Steps that carry their own Fisher-only section (e.g. Skybuilders', Lodestar). In those
        // the blanket/crafter rows are crafter-side only; in a step with no Fisher section (e.g.
        // Dragonsung, Cosmic) the shared rows apply to every job, the Fisher included.
        var fisherSteps = isTools ? StepsWithFisherSection(sheet) : null;

        foreach (var row in sheet.Materials)
        {
            var step = row.Step?.Trim();
            var material = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(step) || !MaterialFilters.IsTrackableMaterial(material))
            {
                continue;
            }

            if (IsQuestRewardRow(row))
            {
                continue;
            }

            if (row.Purchase is { Unit: > 0 } purchase)
            {
                purchaseByMaterial.TryAdd(material!, purchase);
            }

            if (isTools && !seenStepMaterial.Add($"{step}|{material}"))
            {
                continue;
            }

            var perUnit = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            if (perUnit == 0)
            {
                continue;
            }

            var catalogStep = ResolveCatalogStep(step!);
            if (!stepInfo.TryGetValue(catalogStep, out var info) && !stepInfo.TryGetValue(step!, out info))
            {
                continue;
            }

            var jobsNeeding = isTools
                ? CountFlaggedJobsNeeding(row.Jobs, info.Status.Line, info.Tier, ownership, fisherSteps!.Contains(step!))
                : Math.Max(0, info.Status.Line.Jobs - info.Status.ReachedPerStep[info.Tier]);

            var source = sources.TryGetValue(material!, out var mappedSource) ? mappedSource : step!;

            var need = perUnit * (uint)jobsNeeding;
            if (need == 0)
            {
                continue;
            }

            var key = (source, material!);
            if (accumulated.TryGetValue(key, out var existing))
            {
                accumulated[key] = (existing.Need + need, Math.Min(existing.Order, info.Order), jobsNeeding, catalogStep);
            }
            else
            {
                accumulated[key] = (need, info.Order, jobsNeeding, catalogStep);
                keyOrder.Add(key);
            }
        }

        List<ShoppingMaterialRow> result = new(keyOrder.Count);
        foreach (var key in keyOrder)
        {
            (var need, var stepOrder, var jobsNeeding, var catalogStep) = accumulated[key];
            var itemIds = materialIdsByName.TryGetValue(key.Material, out var ids) ? ids : [];
            var resolved = itemIds.Count > 0;
            var ownedInventory = SumOwned(itemIds, ownedLookup);
            var ownedQuestCredit = QuestCreditFor(
                catalogStep,
                key.Material,
                jobsNeeding,
                questRewards,
                ownedLookup);
            result.Add(new()
            {
                Step = key.Source,
                StepOrder = stepOrder,
                Material = key.Material,
                DisplayMaterial = ItemDisplayNames.Label(itemIds, key.Material),
                Need = need,
                OwnedInventory = ownedInventory,
                OwnedQuestCredit = ownedQuestCredit,
                Resolved = resolved,
                Purchase = purchaseByMaterial.GetValueOrDefault(key.Material)
            });
        }

        return result;
    }

    public static uint SumOwned(IReadOnlyList<uint> itemIds, Func<uint, uint> ownedLookup)
    {
        var total = 0u;
        for (var i = 0; i < itemIds.Count; i++)
        {
            total += ownedLookup(itemIds[i]);
        }

        return total;
    }

    public static bool IsQuestRewardRow(ExpansionMaterialRow row) =>
        string.Equals(row.Role, "quest", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.Role, "covers", StringComparison.OrdinalIgnoreCase);

    public static string ResolveCatalogStep(string sheetStep) =>
        MaterialStepAliases.TryGetValue(sheetStep, out var mapped) ? mapped : sheetStep;

    public sealed class QuestRewardIndex
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, uint>>> CoversByStep { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<uint>> ProductIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> ProductsByStep { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public static QuestRewardIndex BuildQuestRewardIndex(ExpansionSheet sheet)
    {
        QuestRewardIndex index = new();
        foreach (var row in sheet.Materials)
        {
            var step = row.Step?.Trim();
            if (string.IsNullOrWhiteSpace(step))
            {
                continue;
            }

            var catalogStep = ResolveCatalogStep(step);

            if (string.Equals(row.Role, "quest", StringComparison.OrdinalIgnoreCase))
            {
                var product = row.Material?.Trim();
                if (!string.IsNullOrWhiteSpace(product) && row.MaterialIds.Count > 0)
                {
                    index.ProductIds.TryAdd(product, row.MaterialIds);
                    if (!index.ProductsByStep.TryGetValue(catalogStep, out var products))
                    {
                        products = [];
                        index.ProductsByStep[catalogStep] = products;
                    }

                    products.Add(product);
                }

                continue;
            }

            if (!string.Equals(row.Role, "covers", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var craftOf = row.CraftOf?.Trim();
            var material = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(craftOf) || string.IsNullOrWhiteSpace(material))
            {
                continue;
            }

            var per = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            if (per == 0)
            {
                continue;
            }

            if (!index.CoversByStep.TryGetValue(catalogStep, out var byProduct))
            {
                byProduct = new Dictionary<string, Dictionary<string, uint>>(StringComparer.OrdinalIgnoreCase);
                index.CoversByStep[catalogStep] = byProduct;
            }

            if (!byProduct.TryGetValue(craftOf, out var covers))
            {
                covers = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                byProduct[craftOf] = covers;
            }

            covers.TryGetValue(material, out var existing);
            covers[material] = existing + per;
        }

        return index;
    }

    public static uint QuestCreditFor(
        string catalogStep,
        string material,
        int weaponsCap,
        QuestRewardIndex questRewards,
        Func<uint, uint> ownedLookup)
    {
        if (weaponsCap <= 0
            || !questRewards.CoversByStep.TryGetValue(catalogStep, out var byProduct))
        {
            return 0;
        }

        var cap = (uint)weaponsCap;
        var credit = 0u;
        foreach (var (product, covers) in byProduct)
        {
            if (!covers.TryGetValue(material, out var per) || per == 0)
            {
                continue;
            }

            if (!questRewards.ProductIds.TryGetValue(product, out var productIds))
            {
                continue;
            }

            var productOwned = SumOwned(productIds, ownedLookup);
            credit += Math.Min(productOwned, cap) * per;
        }

        return credit;
    }

    public static IReadOnlyList<ShoppingQuestRewardRow> GetQuestRewards(
        string catalogStep,
        QuestRewardIndex questRewards,
        Func<uint, uint> ownedLookup)
    {
        if (!questRewards.ProductsByStep.TryGetValue(catalogStep, out var products))
        {
            return [];
        }

        List<ShoppingQuestRewardRow> result = new(products.Count);
        foreach (var product in products)
        {
            if (!questRewards.ProductIds.TryGetValue(product, out var itemIds))
            {
                continue;
            }

            result.Add(new()
            {
                CatalogStep = catalogStep,
                Material = product,
                DisplayMaterial = ItemDisplayNames.Label(itemIds, product),
                Owned = SumOwned(itemIds, ownedLookup),
                Resolved = itemIds.Count > 0
            });
        }

        return result;
    }

    private static int CountFlaggedJobsNeeding(IReadOnlyList<bool?> jobs, RelicLine line, int tier, RelicOwnership ownership, bool stepHasFisherSection)
    {
        var count = 0;
        for (var slot = 0; slot < line.Jobs; slot++)
        {
            if (ToolMaterialAppliesToSlot(jobs, slot, stepHasFisherSection) && !ownership.IsStepDoneOrManual(line, slot, tier))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsFisherOnly(IReadOnlyList<bool?> jobs)
    {
        if (jobs.Count <= FisherSlot || jobs[FisherSlot] != true)
        {
            return false;
        }

        for (var i = 0; i < FisherSlot && i < jobs.Count; i++)
        {
            if (jobs[i] == true)
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> StepsWithFisherSection(ExpansionSheet sheet)
    {
        HashSet<string> steps = new(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Materials)
        {
            var step = row.Step?.Trim();
            if (!string.IsNullOrWhiteSpace(step) && IsFisherOnly(row.Jobs))
            {
                steps.Add(step!);
            }
        }

        return steps;
    }

    public static bool ToolStepHasFisherSection(ExpansionSheet sheet, string step)
    {
        foreach (var row in sheet.Materials)
        {
            if (string.Equals(row.Step?.Trim(), step, StringComparison.OrdinalIgnoreCase) && IsFisherOnly(row.Jobs))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ToolMaterialAppliesToSlot(IReadOnlyList<bool?> jobs, int slotIndex, bool stepHasFisherSection)
    {
        if (slotIndex < 0)
        {
            return true;
        }

        var fisherOnly = IsFisherOnly(jobs);
        var fisherFlagged = jobs.Count > FisherSlot && jobs[FisherSlot] == true;
        var crafterFlagged = false;
        for (var i = 0; i < FisherSlot && i < jobs.Count; i++)
        {
            if (jobs[i] == true)
            {
                crafterFlagged = true;
                break;
            }
        }

        if (slotIndex == FisherSlot)
        {
            // With a Fisher section, the Fisher only needs its own parts. Without one, it shares
            // the step's blanket/flagged materials like every other job.
            return stepHasFisherSection
                ? fisherOnly
                : !crafterFlagged || fisherFlagged;
        }

        // Crafters / miner / botanist never pick up the Fisher-only parts. Honour explicit
        // crafter/gatherer columns; a blanket row (none set) covers them all.
        if (fisherOnly)
        {
            return false;
        }

        return !crafterFlagged || (slotIndex < jobs.Count && jobs[slotIndex] == true);
    }
}

internal static class MaterialFilters
{
    // Keep in sync with data/build_material_aliases.py SKIP.
    private static readonly HashSet<string> NonItemLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Crafters",
        "Fisher",
        "Miner & Botanist",
        "Cosmic",
        "Stellar",
        "Hyper",
        "Select Material",
        "You just do Cosmic Exploration."
    };

    public static bool IsTrackableMaterial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Contains('\n', StringComparison.Ordinal)
            || NonItemLabels.Contains(trimmed)
            || trimmed.StartsWith("First ", StringComparison.Ordinal)
            || trimmed.Contains("assume the maximum", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
