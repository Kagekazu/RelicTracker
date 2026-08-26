namespace RelicTracker.Framework;

public sealed class RelicLineStatus
{
    public required RelicLine Line { get; init; }

    public required int[] ReachedPerStep { get; init; }

    public int TierCount { get; init; }

    public int JobsComplete => TierCount > 0 ? ReachedPerStep[TierCount - 1] : 0;

    public int JobsNotStarted => Math.Max(0, Line.Jobs - (TierCount > 0 ? ReachedPerStep[0] : 0));

    public int StepsDone
    {
        get
        {
            var sum = 0;
            for (var tier = 0; tier < TierCount; tier++)
            {
                sum += ReachedPerStep[tier];
            }

            return sum;
        }
    }

    public int StepsTotal => Line.Jobs * TierCount;

    public float Percent => StepsTotal > 0 ? (float)StepsDone / StepsTotal : 0f;

    public bool IsComplete => Line.Jobs > 0 && JobsComplete >= Line.Jobs;

    public int JobsAtStep(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= TierCount)
        {
            return 0;
        }

        var atOrBelow = ReachedPerStep[tierIndex];
        var above = tierIndex + 1 < TierCount ? ReachedPerStep[tierIndex + 1] : 0;
        return Math.Max(0, atOrBelow - above);
    }
}

public sealed class RelicProgressSummary
{
    public int LinesComplete { get; init; }
    public int LineCount { get; init; }
    public int JobsComplete { get; init; }
    public int JobsTotal { get; init; }
    public int StepsDone { get; init; }
    public int StepsTotal { get; init; }
    public float Percent => StepsTotal > 0 ? (float)StepsDone / StepsTotal : 0f;
}

public sealed class RelicOwnership
(
    FfxivCollectSnapshot snapshot,
    HashSet<string>? manualDone = null,
    HashSet<string>? manualArmor = null,
    HashSet<string>? inventoryDone = null,
    HashSet<string>? inventoryArmor = null)
{
    private readonly HashSet<string> inventoryDone = inventoryDone ?? new(StringComparer.Ordinal);
    private readonly HashSet<string> inventoryArmor = inventoryArmor ?? new(StringComparer.Ordinal);
    private readonly HashSet<string> manualArmor = manualArmor ?? new(StringComparer.Ordinal);
    private readonly HashSet<string> manualDone = manualDone ?? new(StringComparer.Ordinal);

    private readonly HashSet<string> owned = snapshot.Owned
        .Where(relic => relic.Type is not null && relic.Order > 0)
        .Select(relic => $"{relic.Type!.Name}#{relic.Order}")
        .ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<string, int> ownedCountByType = snapshot.Owned
        .Where(relic => relic.Type is not null)
        .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public int OwnedCount(string collectType) =>
        ownedCountByType.TryGetValue(collectType, out var count) ? count : 0;

    private int ManualPieceCount(string collectType, int pieces) =>
        CountKeys(manualArmor, collectType, pieces);

    private int InventoryPieceCount(string collectType, int pieces) =>
        CountKeys(inventoryArmor, collectType, pieces);

    private static int CountKeys(HashSet<string> keys, string collectType, int pieces)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        var n = 0;
        for (var i = 0; i < pieces; i++)
        {
            if (keys.Contains($"{collectType}|{i}"))
            {
                n++;
            }
        }

        return n;
    }

    public int OwnedPieceCount(string collectType, int pieces) =>
        Math.Max(
            Math.Min(pieces, OwnedCount(collectType)),
            Math.Max(ManualPieceCount(collectType, pieces), InventoryPieceCount(collectType, pieces)));

    public bool IsArmorPieceOwned(string collectType, int pieceIndex) =>
        inventoryArmor.Contains($"{collectType}|{pieceIndex}")
        || manualArmor.Contains($"{collectType}|{pieceIndex}");

    public bool IsCollectStepDone(RelicLine line, int slotIndex, int tier)
    {
        if (line.Jobs <= 0 || slotIndex < 0 || tier < 0)
        {
            return false;
        }

        var order = (tier * line.Jobs) + slotIndex + 1;
        return owned.Contains($"{line.CollectType}#{order}");
    }

    public bool IsInventoryStepDone(RelicLine line, int slotIndex, int tier)
    {
        var jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
                              && inventoryDone.Contains($"{line.CollectType}|{jobs[slotIndex]}|{tier}");
    }

    public bool IsStepDone(RelicLine line, int slotIndex, int tier) =>
        IsCollectStepDone(line, slotIndex, tier) || IsInventoryStepDone(line, slotIndex, tier);

    public bool IsStepDoneOrManual(RelicLine line, int slotIndex, int tier)
    {
        if (IsStepDone(line, slotIndex, tier))
        {
            return true;
        }

        if (manualDone.Count == 0)
        {
            return false;
        }

        var jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
                              && manualDone.Contains($"{line.CollectType}|{jobs[slotIndex]}|{tier}");
    }
}

public static class RelicStatusService
{
    public static IReadOnlyList<RelicLineStatus> Build(
        RelicOwnership ownership,
        RelicCatalog catalog,
        bool hidePhyseos = false)
    {
        List<RelicLineStatus> statuses = new(catalog.Lines.Count);
        foreach (var line in catalog.Lines)
        {
            var reached = new int[line.TierCount];
            for (var tier = 0; tier < line.TierCount; tier++)
            {
                var count = 0;
                for (var slot = 0; slot < line.Jobs; slot++)
                {
                    if (ownership.IsStepDoneOrManual(line, slot, tier))
                    {
                        count++;
                    }
                }

                reached[tier] = count;
            }

            statuses.Add(new()
            {
                Line = line,
                ReachedPerStep = reached,
                TierCount = line.EffectiveTierCount(hidePhyseos)
            });
        }

        return statuses;
    }

    public static RelicProgressSummary Summarize(IEnumerable<RelicLineStatus> statuses)
    {
        var list = statuses as IReadOnlyList<RelicLineStatus> ?? [.. statuses];
        return new()
        {
            LineCount = list.Count,
            LinesComplete = list.Count(status => status.IsComplete),
            JobsComplete = list.Sum(status => status.JobsComplete),
            JobsTotal = list.Sum(status => status.Line.Jobs),
            StepsDone = list.Sum(status => status.StepsDone),
            StepsTotal = list.Sum(status => status.StepsTotal)
        };
    }

    public static HashSet<string> BuildStepDoneKeys(RelicCatalog catalog, Func<uint, uint> ownedLookup)
    {
        HashSet<string> done = new(StringComparer.Ordinal);
        foreach (RelicLine line in catalog.Lines)
        {
            IReadOnlyList<string> jobs = line.EffectiveJobList;
            for (int slot = 0; slot < line.Jobs && slot < jobs.Count; slot++)
            {
                for (int tier = 0; tier < line.TierCount; tier++)
                {
                    uint relicId = line.RelicId(slot, tier);
                    if (relicId == 0
                        || (!IsRelicOrReplicaOwned(relicId, line.RelicReplicas(slot, tier), ownedLookup)
                            && !WksCosmicTools.CreditsStep(line, slot, tier)))
                    {
                        continue;
                    }

                    for (int completedTier = 0; completedTier <= tier; completedTier++)
                    {
                        done.Add($"{line.CollectType}|{jobs[slot]}|{completedTier}");
                    }
                }
            }
        }

        return done;
    }

    public static HashSet<string> BuildArmorPieceDoneKeys(RelicCatalog catalog, Func<uint, uint> ownedLookup)
    {
        HashSet<string> done = new(StringComparer.Ordinal);
        foreach (ArmorLine armorLine in catalog.ArmorLines)
        {
            foreach (ArmorSet set in armorLine.Sets)
            {
                for (int tierIndex = 0; tierIndex < set.Tiers.Count; tierIndex++)
                {
                    ArmorTier tier = set.Tiers[tierIndex];
                    int pieceCount = Math.Min(tier.Pieces, tier.PieceIds.Count);
                    for (int index = 0; index < pieceCount; index++)
                    {
                        uint pieceId = tier.PieceIds[index];
                        if (pieceId == 0 || ownedLookup(pieceId) == 0)
                        {
                            continue;
                        }

                        ArmorCostCalculator.AddOwnedPieceKeys(armorLine, set, tierIndex, index, done);
                    }
                }
            }
        }

        return done;
    }

    private static bool IsRelicOrReplicaOwned(
        uint relicId,
        IReadOnlyList<uint> replicaIds,
        Func<uint, uint> ownedLookup)
    {
        if (relicId > 0 && ownedLookup(relicId) > 0)
        {
            return true;
        }

        foreach (uint replicaId in replicaIds)
        {
            if (replicaId > 0 && ownedLookup(replicaId) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
