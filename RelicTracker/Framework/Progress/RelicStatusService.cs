namespace RelicTracker.Framework;

public static class ProgressKeys
{
    public static string Step(string collectType, string job, int tier) => $"{collectType}|{job}|{tier}";

    public static string ArmorPiece(string collectType, int pieceIndex) => $"{collectType}|{pieceIndex}";
}

public sealed class RelicLineStatus
{
    public required RelicLine Line { get; init; }

    public required int[] ReachedPerStep { get; init; }

    public int TierCount { get; init; }

    public int JobsComplete => TierCount > 0 ? ReachedPerStep[TierCount - 1] : 0;

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

    // FFXIV Collect orders per series. Armour order is piece index + 1; weapon order is tier * jobs + slot + 1.
    private readonly Dictionary<string, HashSet<int>> ownedOrders = snapshot.Owned
        .Where(relic => relic.Type is not null && relic.Order > 0)
        .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Select(relic => relic.Order).ToHashSet(), StringComparer.Ordinal);

    public int OwnedPieceCount(string collectType, int pieces)
    {
        var count = 0;
        for (var i = 0; i < pieces; i++)
        {
            if (IsArmorPieceOwned(collectType, i))
            {
                count++;
            }
        }

        return count;
    }

    public bool IsArmorPieceOwned(string collectType, int pieceIndex) =>
        inventoryArmor.Contains(ProgressKeys.ArmorPiece(collectType, pieceIndex))
        || manualArmor.Contains(ProgressKeys.ArmorPiece(collectType, pieceIndex))
        || (ownedOrders.TryGetValue(collectType, out var orders) && orders.Contains(pieceIndex + 1));

    // A later tier on FFXIV Collect means every earlier tier of that job was done too.
    public bool IsCollectStepDone(RelicLine line, int slotIndex, int tier)
    {
        if (line.Jobs <= 0 || slotIndex < 0 || tier < 0 || !ownedOrders.TryGetValue(line.CollectType, out var orders))
        {
            return false;
        }

        for (var t = tier; t < line.TierCount; t++)
        {
            if (orders.Contains((t * line.Jobs) + slotIndex + 1))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInventoryStepDone(RelicLine line, int slotIndex, int tier)
    {
        var jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
                              && inventoryDone.Contains(ProgressKeys.Step(line.CollectType, jobs[slotIndex], tier));
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
                              && manualDone.Contains(ProgressKeys.Step(line.CollectType, jobs[slotIndex], tier));
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
                        done.Add(ProgressKeys.Step(line.CollectType, jobs[slot], completedTier));
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
