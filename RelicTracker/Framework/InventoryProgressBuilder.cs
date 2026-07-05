namespace RelicTracker.Framework;

/// <summary>Marks relic weapon/tool steps and armor pieces done from Allagan Tools inventory.</summary>
public static class InventoryProgressBuilder
{
    public static HashSet<string> BuildStepDoneKeys(
        RelicCatalog catalog,
        Func<uint, uint> ownedLookup)
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
                        || !IsRelicOrReplicaOwned(relicId, line.RelicReplicas(slot, tier), ownedLookup))
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

    public static HashSet<string> BuildArmorPieceDoneKeys(
        RelicCatalog catalog,
        Func<uint, uint> ownedLookup)
    {
        HashSet<string> done = new(StringComparer.Ordinal);
        foreach (ArmorLine armorLine in catalog.ArmorLines)
        {
            foreach (ArmorTier tier in armorLine.AllTiers)
            {
                int pieceCount = Math.Min(tier.Pieces, tier.PieceIds.Count);
                for (int index = 0; index < pieceCount; index++)
                {
                    uint pieceId = tier.PieceIds[index];
                    if (pieceId == 0 || !IsOwned(pieceId, ownedLookup))
                    {
                        continue;
                    }

                    done.Add($"{tier.CollectType}|{index}");
                }
            }
        }

        return done;
    }

    private static bool IsOwned(uint itemId, Func<uint, uint> ownedLookup) =>
        itemId > 0 && ownedLookup(itemId) > 0;

    private static bool IsRelicOrReplicaOwned(
        uint relicId,
        IReadOnlyList<uint> replicaIds,
        Func<uint, uint> ownedLookup)
    {
        if (IsOwned(relicId, ownedLookup))
        {
            return true;
        }

        foreach (uint replicaId in replicaIds)
        {
            if (IsOwned(replicaId, ownedLookup))
            {
                return true;
            }
        }

        return false;
    }
}
