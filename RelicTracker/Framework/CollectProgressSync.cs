namespace RelicTracker.Framework;

/// <summary>
/// Derives per-job relic step completion from FFXIV Collect owned relics.
/// </summary>
public sealed class CollectProgressSync
{
    private static readonly Dictionary<string, string> CollectTypeToWynExpansion =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["A Relic Reborn"] = "ARR",
            ["Anima Weapons"] = "HW",
            ["Eureka Weapons"] = "SB",
            ["Resistance Weapons"] = "ShB",
            ["Manderville Weapons"] = "EW",
            ["Phantom Weapons"] = "DT",
            ["Lucis Tools"] = "DoHDoL",
            ["Skysteel Tools"] = "DoHDoL",
            ["Resplendent Tools"] = "DoHDoL",
            ["Splendorous Tools"] = "DoHDoL",
            ["Cosmic Tools"] = "DoHDoL",
        };

    private readonly object gate = new();
    private ulong syncedCharacterId;
    private DateTime? syncedRefreshUtc;
    private Dictionary<string, Dictionary<string, int[]>> maxTierByCollectType = new(StringComparer.Ordinal);

    public bool IsActive(ulong characterId)
    {
        lock (gate)
        {
            return characterId > 0 && syncedCharacterId == characterId && syncedRefreshUtc is not null;
        }
    }

    public void EnsureSynced(
        ulong characterId,
        FfxivCollectSnapshot snapshot,
        DateTime? refreshUtc,
        RelicDataService data)
    {
        if (characterId == 0 || snapshot.CharacterId != characterId || refreshUtc is null)
        {
            return;
        }

        lock (gate)
        {
            if (syncedCharacterId == characterId && syncedRefreshUtc == refreshUtc)
            {
                return;
            }
        }

        Rebuild(characterId, snapshot, data, refreshUtc.Value);
    }

    private void Rebuild(
        ulong characterId,
        FfxivCollectSnapshot snapshot,
        RelicDataService data,
        DateTime refreshUtc)
    {
        var tiers = new Dictionary<string, Dictionary<string, int[]>>(StringComparer.Ordinal);

        foreach (var expansionId in data.Expansions.Keys)
        {
            tiers[expansionId] = new Dictionary<string, int[]>(StringComparer.Ordinal);
        }

        foreach (var relic in snapshot.Owned)
        {
            if (relic.Order <= 0 || relic.Type is null)
            {
                continue;
            }

            if (!CollectTypeToWynExpansion.TryGetValue(relic.Type.Name, out var expansionId))
            {
                continue;
            }

            if (!tiers.TryGetValue(expansionId, out var typeTiers))
            {
                continue;
            }

            var jobsPerTier = relic.Type.Jobs ?? data.Expansions[expansionId].JobCount;
            if (jobsPerTier <= 0)
            {
                continue;
            }

            var jobIndex = (relic.Order - 1) % jobsPerTier;
            var tierIndex = (relic.Order - 1) / jobsPerTier;
            if (jobIndex < 0)
            {
                continue;
            }

            if (!typeTiers.TryGetValue(relic.Type.Name, out var jobTiers)
                || jobTiers.Length < data.Expansions[expansionId].JobCount)
            {
                jobTiers = new int[data.Expansions[expansionId].JobCount];
                typeTiers[relic.Type.Name] = jobTiers;
            }

            if (jobIndex >= jobTiers.Length)
            {
                continue;
            }

            jobTiers[jobIndex] = Math.Max(jobTiers[jobIndex], tierIndex);
        }

        lock (gate)
        {
            syncedCharacterId = characterId;
            syncedRefreshUtc = refreshUtc;
            maxTierByCollectType = tiers;
        }

        Svc.Log.Information(
            "[RelicTracker] Collect progress synced for character {CharacterId} ({Owned} owned relics).",
            characterId,
            snapshot.Owned.Count);
    }

    public bool IsStepCompleteForJob(string expansionId, string step, int jobIndex)
    {
        lock (gate)
        {
            if (!CollectStepMap.TryGetRequirement(expansionId, step, out var requirement)
                || !maxTierByCollectType.TryGetValue(expansionId, out var typeTiers)
                || jobIndex < 0)
            {
                return false;
            }

            if (!typeTiers.TryGetValue(requirement.CollectType, out var jobTiers)
                || jobIndex >= jobTiers.Length)
            {
                return false;
            }

            return jobTiers[jobIndex] >= requirement.RequiredTier;
        }
    }
}
