namespace RelicTracker.Framework;

/// <summary>Marks relic weapon/tool steps done from Allagan Tools inventory (replicas included).</summary>
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
}
