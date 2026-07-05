namespace RelicTracker.Framework;

/// <summary>Marks relic weapon/tool steps and armor pieces done from Allagan Tools inventory.</summary>
public static class InventoryProgressBuilder
{
    public static HashSet<string> BuildStepDoneKeys(
        RelicCatalog catalog,
        ItemResolver items,
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
                    string? relicName = line.RelicName(slot, tier);
                    if (string.IsNullOrWhiteSpace(relicName)
                        || !items.IsRelicOrReplicaOwned(relicName, ownedLookup))
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
        ItemResolver items,
        Func<uint, uint> ownedLookup)
    {
        HashSet<string> done = new(StringComparer.Ordinal);
        foreach (ArmorLine armorLine in catalog.ArmorLines)
        {
            foreach (ArmorTier tier in armorLine.AllTiers)
            {
                int pieceCount = Math.Min(tier.Pieces, tier.PieceNames.Count);
                for (int index = 0; index < pieceCount; index++)
                {
                    string pieceName = tier.PieceNames[index];
                    if (string.IsNullOrWhiteSpace(pieceName)
                        || !items.IsItemOwned(pieceName, ownedLookup))
                    {
                        continue;
                    }

                    done.Add($"{tier.CollectType}|{index}");
                }
            }
        }

        return done;
    }
}
