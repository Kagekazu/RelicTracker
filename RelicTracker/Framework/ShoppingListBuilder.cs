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

    public string? Obtain { get; init; }

    public string? ObtainVia { get; init; }

    public bool OneTime { get; init; }

    public bool Craftable { get; init; }

    /// <summary>Relic steps this row is needed for (Step can be a farming zone instead).</summary>
    public IReadOnlyList<string> ForSteps { get; init; } = [];

    public string ObtainLabel => MaterialObtain.Label(Obtain, ObtainVia);

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

    public static List<ShoppingMaterialRow> Build(
        string expansionId,
        ExpansionSheet sheet,
        IReadOnlyList<RelicLineStatus> statuses,
        RelicOwnership ownership,
        Func<uint, uint> ownedLookup,
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, IReadOnlyList<uint>> materialIdsByName,
        string? lineFilter = null,
        IReadOnlyDictionary<string, string>? lucisFirstJobs = null)
    {
        var isTools = string.Equals(expansionId, ExpansionNames.ToolsId, StringComparison.Ordinal);

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

        // Lucis Tools: only the first job per category farms materials; the rest buy their tool
        // from a Calamity Salvager once that first Lucis tool exists.
        Dictionary<RelicLine, HashSet<int>> skippedSlots = [];
        Dictionary<RelicLine, IReadOnlyList<int>> salvagerSlots = [];
        foreach (var line in stepInfo.Values.Select(info => info.Status.Line).Distinct())
        {
            if (isTools && LucisToolShortcut.Applies(line))
            {
                skippedSlots[line] = LucisToolShortcut.SkippedSlots(line, ownership, lucisFirstJobs);
                salvagerSlots[line] = LucisToolShortcut.SalvagerSlots(line, ownership);
            }
        }

        Dictionary<(string Source, string Material), (uint Need, int Order, int JobsNeeding, string CatalogStep)> accumulated = [];
        List<(string Source, string Material)> keyOrder = [];
        HashSet<string> seenStepMaterial = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MaterialPurchase> purchaseByMaterial = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(string Source, string Material), ExpansionMaterialRow> firstRowByKey = [];
        Dictionary<(string Source, string Material), List<string>> stepsByKey = [];

        // Steps that carry their own Fisher-only section (e.g. Skybuilders', Lodestar). In those
        // the blanket/crafter rows are crafter-side only; in a step with no Fisher section (e.g.
        // Dragonsung, Cosmic) the shared rows apply to every job, the Fisher included.
        var fisherSteps = isTools ? StepsWithFisherSection(sheet) : null;

        foreach (var row in sheet.Materials)
        {
            var step = row.Step?.Trim();
            var material = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(step) || string.IsNullOrWhiteSpace(material))
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

            // Skip only exact repeats; the same material for another class (Darksteel Ore for both
            // BSM and ARM) or under another precraft is a separate need and sums below.
            if (isTools && !seenStepMaterial.Add($"{step}|{material}|{row.CraftOf}|{string.Join(",", row.Jobs)}"))
            {
                continue;
            }

            var perUnit = row.PerUnitCount;
            if (perUnit == 0)
            {
                continue;
            }

            if (!stepInfo.TryGetValue(step!, out var info))
            {
                continue;
            }

            var jobsNeeding = isTools
                ? CountFlaggedJobsNeeding(
                    row.Jobs,
                    info.Status.Line,
                    info.Tier,
                    ownership,
                    fisherSteps!.Contains(step!),
                    skippedSlots.GetValueOrDefault(info.Status.Line))
                : IsJobSpecific(row.Jobs)
                    ? Enumerable.Range(0, info.Status.Line.Jobs).Count(slot =>
                        slot < row.Jobs.Count && row.Jobs[slot] == true
                        && !ownership.IsStepDoneOrManual(info.Status.Line, slot, info.Tier))
                    : Math.Max(0, info.Status.Line.Jobs - info.Status.ReachedPerStep[info.Tier]);

            var source = sources.TryGetValue(material!, out var mappedSource) ? mappedSource : step!;

            // One-time unlocks are needed once, and only until any job has reached the step.
            var need = row.OneTime
                ? (jobsNeeding > 0 && info.Status.ReachedPerStep[info.Tier] == 0 ? perUnit : 0)
                : perUnit * (uint)jobsNeeding;
            if (need == 0)
            {
                continue;
            }

            var key = (source, material!);
            firstRowByKey.TryAdd(key, row);
            if (!stepsByKey.TryGetValue(key, out var keySteps))
            {
                keySteps = [];
                stepsByKey[key] = keySteps;
            }

            if (!keySteps.Contains(step!, StringComparer.OrdinalIgnoreCase))
            {
                keySteps.Add(step!);
            }

            if (accumulated.TryGetValue(key, out var existing))
            {
                accumulated[key] = (existing.Need + need, Math.Min(existing.Order, info.Order), jobsNeeding, step!);
            }
            else
            {
                accumulated[key] = (need, info.Order, jobsNeeding, step!);
                keyOrder.Add(key);
            }
        }

        List<ShoppingMaterialRow> result = new(keyOrder.Count);
        foreach (var key in keyOrder)
        {
            (var need, var stepOrder, var jobsNeeding, var catalogStep) = accumulated[key];
            var firstRow = firstRowByKey[key];
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
                Purchase = purchaseByMaterial.GetValueOrDefault(key.Material),
                Obtain = firstRow.Obtain,
                ObtainVia = firstRow.ObtainVia,
                OneTime = firstRow.OneTime,
                Craftable = firstRow.Craftable,
                ForSteps = stepsByKey[key]
            });
        }

        foreach ((RelicLine line, IReadOnlyList<int> slots) in salvagerSlots)
        {
            var top = line.TierCount - 1;
            var step = line.StepName(top);
            var stepOrder = stepInfo.TryGetValue(step, out var topInfo) ? topInfo.Order : int.MaxValue;
            foreach (var slot in slots)
            {
                uint toolId = line.RelicId(slot, top);
                if (toolId == 0)
                {
                    continue;
                }

                var name = ItemDisplayNames.Label([toolId], $"{line.EffectiveJobList[slot]} Lucis tool");
                result.Add(new()
                {
                    Step = step,
                    StepOrder = stepOrder,
                    Material = name,
                    DisplayMaterial = name,
                    Need = 1,
                    OwnedInventory = ownedLookup(toolId),
                    OwnedQuestCredit = 0,
                    Resolved = true,
                    Purchase = new() { Currency = "gil", Unit = LucisToolShortcut.SalvagerGil },
                    Obtain = "buy",
                    ObtainVia = "Calamity Salvager",
                    ForSteps = [step]
                });
            }
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

    /// <summary>
    ///     Weapon-line rows apply to every job unless they flag only some (the Zodiac base weapons);
    ///     then the flags follow the line's job order.
    /// </summary>
    public static bool IsJobSpecific(IReadOnlyList<bool?> jobs) => jobs.Any(flag => flag != true);

    public static bool IsQuestRewardRow(ExpansionMaterialRow row) =>
        string.Equals(row.Role, "quest", StringComparison.OrdinalIgnoreCase)
        || string.Equals(row.Role, "covers", StringComparison.OrdinalIgnoreCase);

    public sealed class QuestRewardIndex
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, uint>>> CoversByStep { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<uint>> ProductIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> ProductsByStep { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Job flags of each reward row; on tool lines they pick the job slot.</summary>
        public Dictionary<string, IReadOnlyList<bool?>> ProductJobs { get; } =
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

            if (string.Equals(row.Role, "quest", StringComparison.OrdinalIgnoreCase))
            {
                var product = row.Material?.Trim();
                if (!string.IsNullOrWhiteSpace(product) && row.MaterialIds.Count > 0)
                {
                    index.ProductIds.TryAdd(product, row.MaterialIds);
                    index.ProductJobs.TryAdd(product, row.Jobs);
                    if (!index.ProductsByStep.TryGetValue(step, out var products))
                    {
                        products = [];
                        index.ProductsByStep[step] = products;
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

            var per = row.PerUnitCount;
            if (per == 0)
            {
                continue;
            }

            if (!index.CoversByStep.TryGetValue(step, out var byProduct))
            {
                byProduct = new Dictionary<string, Dictionary<string, uint>>(StringComparer.OrdinalIgnoreCase);
                index.CoversByStep[step] = byProduct;
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
        Func<uint, uint> ownedLookup,
        int toolSlot = -1)
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

            if (toolSlot >= 0
                && questRewards.ProductJobs.TryGetValue(product, out var jobs)
                && !ToolMaterialAppliesToSlot(jobs, toolSlot, stepHasFisherSection: true))
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

    private static int CountFlaggedJobsNeeding(
        IReadOnlyList<bool?> jobs,
        RelicLine line,
        int tier,
        RelicOwnership ownership,
        bool stepHasFisherSection,
        IReadOnlySet<int>? skippedSlots)
    {
        var count = 0;
        for (var slot = 0; slot < line.Jobs; slot++)
        {
            if (skippedSlots is not null && skippedSlots.Contains(slot))
            {
                continue;
            }

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
            // Rows that flag the Fisher always apply (its own parts, or shared rows such as the
            // Lucis demimateria). Unflagged blanket rows only apply when the step has no Fisher
            // section; with one, they are crafter-side.
            return fisherFlagged || (!stepHasFisherSection && !crafterFlagged);
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
