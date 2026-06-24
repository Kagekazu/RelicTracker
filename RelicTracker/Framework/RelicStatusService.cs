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

    public RelicOwnership(FfxivCollectSnapshot snapshot)
    {
        owned = snapshot.Owned
            .Where(relic => relic.Type is not null && relic.Order > 0)
            .Select(relic => $"{relic.Type!.Name}#{relic.Order}")
            .ToHashSet(StringComparer.Ordinal);

        ownedCountByType = snapshot.Owned
            .Where(relic => relic.Type is not null)
            .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    /// <summary>How many relics of a given Collect type the character owns (used for armor piece counts).</summary>
    public int OwnedCount(string collectType) =>
        ownedCountByType.TryGetValue(collectType, out var count) ? count : 0;

    /// <summary>True if the character owns the relic for this job slot at this step (tier).</summary>
    public bool IsStepDone(RelicLine line, int slotIndex, int tier)
    {
        if (line.Jobs <= 0 || slotIndex < 0 || tier < 0)
        {
            return false;
        }

        var order = (tier * line.Jobs) + slotIndex + 1;
        return owned.Contains($"{line.CollectType}#{order}");
    }
}

public sealed class RelicStatusService
{
    /// <summary>Builds per-line status from the owned-relic snapshot, keyed by Collect type name.</summary>
    public static IReadOnlyList<RelicLineStatus> Build(FfxivCollectSnapshot snapshot, RelicCatalog catalog)
    {
        var ownedByType = snapshot.Owned
            .Where(relic => relic.Order > 0 && relic.Type is not null)
            .GroupBy(relic => relic.Type!.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var statuses = new List<RelicLineStatus>(catalog.Lines.Count);
        foreach (var line in catalog.Lines)
        {
            var reached = new int[line.TierCount];
            if (line.Jobs > 0 && line.TierCount > 0
                && ownedByType.TryGetValue(line.CollectType, out var owned))
            {
                foreach (var relic in owned)
                {
                    var tier = (relic.Order - 1) / line.Jobs;
                    if (tier >= 0 && tier < line.TierCount)
                    {
                        reached[tier]++;
                    }
                }
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
