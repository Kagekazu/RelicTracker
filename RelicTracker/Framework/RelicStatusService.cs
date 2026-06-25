namespace RelicTracker.Framework;

/// <summary>Progress for a single relic line, derived from FFXIV Collect owned relics.</summary>
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

        var atOrBelow = ReachedPerStep[tierIndex];
        var above = tierIndex + 1 < Line.TierCount ? ReachedPerStep[tierIndex + 1] : 0;
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
public sealed class RelicOwnership
{
    private readonly HashSet<string> owned;
    private readonly Dictionary<string, int> ownedCountByType;

    /// <summary>Live reference to manual step ticks (Configuration.RelicStepDone), keyed CollectType|job|tier.</summary>
    private readonly HashSet<string> manualDone;

    /// <summary>Live reference to manual armor piece ticks (Configuration.ArmorPieceDone), keyed CollectType|pieceIndex.</summary>
    private readonly HashSet<string> manualArmor;

    public RelicOwnership(FfxivCollectSnapshot snapshot, HashSet<string>? manualDone = null, HashSet<string>? manualArmor = null)
    {
        owned = snapshot.Owned
            .Where(relic => relic.Type is not null && relic.Order > 0)
            .Select(relic => $"{relic.Type!.Name}#{relic.Order}")
            .ToHashSet(StringComparer.Ordinal);

        ownedCountByType = snapshot.Owned
            .Where(relic => relic.Type is not null)
            .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        this.manualDone = manualDone ?? new HashSet<string>(StringComparer.Ordinal);
        this.manualArmor = manualArmor ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>How many relics of a given Collect type FFXIV Collect shows owned (armor auto-tracking).</summary>
    public int OwnedCount(string collectType) =>
        ownedCountByType.TryGetValue(collectType, out var count) ? count : 0;

    /// <summary>How many of an armor tier's pieces are manually ticked (used when Collect isn't linked).</summary>
    public int ManualPieceCount(string collectType, int pieces)
    {
        if (manualArmor.Count == 0)
        {
            return 0;
        }

        var n = 0;
        for (var i = 0; i < pieces; i++)
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

    /// <summary>
    /// True if FFXIV Collect shows the relic for this job slot at this step (tier) as owned. This is
    /// the auto-detected source only — manual ticks are kept separate so they stay editable.
    /// </summary>
    public bool IsStepDone(RelicLine line, int slotIndex, int tier)
    {
        if (line.Jobs <= 0 || slotIndex < 0 || tier < 0)
        {
            return false;
        }

        var order = (tier * line.Jobs) + slotIndex + 1;
        return owned.Contains($"{line.CollectType}#{order}");
    }

    /// <summary>
    /// True if the step is done for this slot either from FFXIV Collect OR a manual tick. This is the
    /// unified "done" used by the Overview funnel and the Tracker, so the plugin works standalone
    /// (without a Collect link) off manual ticks alone.
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

        var jobs = line.EffectiveJobList;
        return slotIndex >= 0 && slotIndex < jobs.Count
            && manualDone.Contains($"{line.CollectType}|{jobs[slotIndex]}|{tier}");
    }
}

public sealed class RelicStatusService
{
    /// <summary>
    /// Builds per-line status from ownership (FFXIV Collect plus manual ticks), so the Overview and
    /// Tracker funnel reflect manual progress and work without a Collect link.
    /// </summary>
    public static IReadOnlyList<RelicLineStatus> Build(RelicOwnership ownership, RelicCatalog catalog)
    {
        var statuses = new List<RelicLineStatus>(catalog.Lines.Count);
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

            statuses.Add(new RelicLineStatus
            {
                Line = line,
                ReachedPerStep = reached,
            });
        }

        return statuses;
    }

    public static RelicProgressSummary Summarize(IEnumerable<RelicLineStatus> statuses)
    {
        var list = statuses as IReadOnlyList<RelicLineStatus> ?? statuses.ToList();
        return new RelicProgressSummary
        {
            LineCount = list.Count,
            LinesComplete = list.Count(status => status.IsComplete),
            JobsComplete = list.Sum(status => status.JobsComplete),
            JobsTotal = list.Sum(status => status.Line.Jobs),
            StepsDone = list.Sum(status => status.StepsDone),
            StepsTotal = list.Sum(status => status.StepsTotal),
        };
    }
}
