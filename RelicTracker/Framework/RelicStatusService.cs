namespace RelicTracker.Framework;

/// <summary>Progress for a single relic line, derived from Collect, inventory ownership, and manual ticks.</summary>
public sealed class RelicLineStatus
{
    public required RelicLine Line { get; init; }

    /// <summary>ReachedPerStep[t] = number of jobs that have completed step t (cumulative funnel).</summary>
    public required int[] ReachedPerStep { get; init; }

    public int JobsComplete => Line.TierCount > 0 ? ReachedPerStep[^1] : 0;

    public int JobsStarted => Line.TierCount > 0 ? ReachedPerStep[0] : 0;

    public int JobsNotStarted => Math.Max(0, Line.Jobs - JobsStarted);

    public int StepsDone => ReachedPerStep.Sum();

    public int StepsTotal => Line.Jobs * Line.TierCount;

    public float Percent => StepsTotal > 0 ? (float)StepsDone / StepsTotal : 0f;

    public bool IsComplete => Line.Jobs > 0 && JobsComplete >= Line.Jobs;

    /// <summary>Jobs whose highest completed step is exactly t (i.e. currently working on step t+1).</summary>
    public int JobsAtStep(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= Line.TierCount)
        {
            return 0;
        }

        int atOrBelow = ReachedPerStep[tierIndex];
        int above = tierIndex + 1 < Line.TierCount ? ReachedPerStep[tierIndex + 1] : 0;
        return Math.Max(0, atOrBelow - above);
    }
}

/// <summary>Aggregated totals across a set of relic lines.</summary>
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

/// <summary>Fast lookup of which exact relics a character owns, for per-job step detection.</summary>
public sealed class RelicOwnership(
    FfxivCollectSnapshot snapshot,
    HashSet<string>? manualDone = null,
    HashSet<string>? manualArmor = null,
    HashSet<string>? inventoryDone = null)
{
    /// <summary>Live reference to manual armor piece ticks (Configuration.ArmorPieceDone), keyed CollectType|pieceIndex.</summary>
    private readonly HashSet<string> manualArmor = manualArmor ?? new(StringComparer.Ordinal);

    /// <summary>Live reference to manual step ticks (Configuration.RelicStepDone), keyed CollectType|job|tier.</summary>
    private readonly HashSet<string> manualDone = manualDone ?? new(StringComparer.Ordinal);

    /// <summary>Allagan Tools inventory detections, keyed CollectType|job|tier.</summary>
    private readonly HashSet<string> inventoryDone = inventoryDone ?? new(StringComparer.Ordinal);

    private readonly HashSet<string> owned = snapshot.Owned
            .Where(relic => relic.Type is not null && relic.Order > 0)
            .Select(relic => $"{relic.Type!.Name}#{relic.Order}")
            .ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<string, int> ownedCountByType = snapshot.Owned
            .Where(relic => relic.Type is not null)
            .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    /// <summary>How many relics of a given Collect type FFXIV Collect shows owned (armor auto-tracking).</summary>
    public int OwnedCount(string collectType) =>
        ownedCountByType.TryGetValue(collectType, out int count) ? count : 0;

    /// <summary>How many of an armor tier's pieces are manually ticked (used when Collect isn't linked).</summary>
    public int ManualPieceCount(string collectType, int pieces)
    {
        if (manualArmor.Count == 0)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < pieces; i++)
        {
            if (manualArmor.Contains($"{collectType}|{i}"))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Effective owned pieces for an armor tier — FFXIV Collect or manual ticks, whichever is higher.</summary>
    public int OwnedPieceCount(string collectType, int pieces) =>
        Math.Max(Math.Min(pieces, OwnedCount(collectType)), ManualPieceCount(collectType, pieces));

    public bool IsCollectStepDone(RelicLine line, int slotIndex, int tier)
    {
        if (line.Jobs <= 0 || slotIndex < 0 || tier < 0)
        {
            return false;
        }

        int order = (tier * line.Jobs) + slotIndex + 1;
        return owned.Contains($"{line.CollectType}#{order}");
    }

    public bool IsInventoryStepDone(RelicLine line, int slotIndex, int tier)
    {
        IReadOnlyList<string> jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
                              && inventoryDone.Contains($"{line.CollectType}|{jobs[slotIndex]}|{tier}");
    }

    /// <summary>
    ///     True if FFXIV Collect or Allagan Tools inventory shows this step as owned. Manual ticks are
    ///     kept separate so they remain editable fallbacks.
    /// </summary>
    public bool IsStepDone(RelicLine line, int slotIndex, int tier) =>
        IsCollectStepDone(line, slotIndex, tier) || IsInventoryStepDone(line, slotIndex, tier);

    /// <summary>
    ///     True if the step is done for this slot either from FFXIV Collect OR a manual tick. This is the
    ///     unified "done" used by the Overview funnel and the Tracker, so the plugin works standalone
    ///     (without a Collect link) off manual ticks alone.
    /// </summary>
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

        IReadOnlyList<string> jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
                              && manualDone.Contains($"{line.CollectType}|{jobs[slotIndex]}|{tier}");
    }
}

public sealed class RelicStatusService
{
    /// <summary>
    ///     Builds per-line status from ownership (FFXIV Collect, Allagan Tools inventory, and manual
    ///     ticks), so the Overview and Tracker funnel work without a Collect link.
    /// </summary>
    public static IReadOnlyList<RelicLineStatus> Build(RelicOwnership ownership, RelicCatalog catalog)
    {
        List<RelicLineStatus> statuses = new(catalog.Lines.Count);
        foreach (RelicLine line in catalog.Lines)
        {
            int[] reached = new int[line.TierCount];
            for (int tier = 0; tier < line.TierCount; tier++)
            {
                int count = 0;
                for (int slot = 0; slot < line.Jobs; slot++)
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
                ReachedPerStep = reached
            });
        }

        return statuses;
    }

    public static RelicProgressSummary Summarize(IEnumerable<RelicLineStatus> statuses)
    {
        IReadOnlyList<RelicLineStatus> list = statuses as IReadOnlyList<RelicLineStatus> ?? [.. statuses];
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
}
