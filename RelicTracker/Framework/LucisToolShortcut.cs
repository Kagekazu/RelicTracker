namespace RelicTracker.Framework;

/// <summary>
///     Lucis Tools: once one crafter and one gatherer tool reach Lucis, a Calamity Salvager sells
///     every other Lucis tool in that category for 1,000 gil (one achievement per category). So per
///     category only the first job needs the Supra/Lucis materials; the rest buy their tool after.
/// </summary>
public static class LucisToolShortcut
{
    public const string CollectType = "Lucis Tools";
    public const int SalvagerGil = 1_000;

    public static readonly IReadOnlyList<string> CategoryNames = ["crafter", "gatherer"];

    private static readonly Dictionary<string, int[]> Categories = new(StringComparer.Ordinal)
    {
        ["crafter"] = [0, 1, 2, 3, 4, 5, 6, 7],
        ["gatherer"] = [8, 9, 10],
    };

    public static bool Applies(RelicLine line) =>
        string.Equals(line.CollectType, CollectType, StringComparison.Ordinal) && line.TierCount > 0;

    /// <summary>Crafter or gatherer, for a job slot on the Lucis line.</summary>
    public static string CategoryFor(int slot) =>
        Categories.FirstOrDefault(category => category.Value.Contains(slot)).Key ?? "crafter";

    /// <summary>True once any job in the category has its Lucis tool.</summary>
    public static bool IsCategoryUnlocked(RelicLine line, RelicOwnership ownership, string category) =>
        Applies(line)
        && Categories.TryGetValue(category, out var slots)
        && slots.Any(slot => slot < line.Jobs && ownership.IsStepDoneOrManual(line, slot, line.TierCount - 1));

    public static bool IsUnlocked(RelicLine line, RelicOwnership ownership, int slot) =>
        IsCategoryUnlocked(line, ownership, CategoryFor(slot));

    /// <summary>Jobs in the category that don't have their Lucis tool yet.</summary>
    public static IReadOnlyList<int> UnfinishedSlots(RelicLine line, RelicOwnership ownership, string category) =>
        Applies(line) && Categories.TryGetValue(category, out var slots)
            ? [.. slots.Where(slot => slot < line.Jobs && !ownership.IsStepDoneOrManual(line, slot, line.TierCount - 1))]
            : [];

    /// <summary>
    ///     The job that makes the category's first Lucis tool: the preferred job if it's still
    ///     unfinished, otherwise the furthest along (first slot on a tie). -1 when none is unfinished.
    /// </summary>
    public static int FirstSlot(RelicLine line, RelicOwnership ownership, string category, string? preferredJob)
    {
        var unfinished = UnfinishedSlots(line, ownership, category);
        if (unfinished.Count == 0)
        {
            return -1;
        }

        var jobs = line.EffectiveJobList;
        if (!string.IsNullOrEmpty(preferredJob))
        {
            foreach (var slot in unfinished)
            {
                if (slot < jobs.Count && string.Equals(jobs[slot], preferredJob, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }
        }

        return unfinished.OrderByDescending(slot => StepsReached(line, ownership, slot)).ThenBy(slot => slot).First();
    }

    /// <summary>
    ///     Jobs whose Supra/Lucis materials aren't counted: every unfinished job in an unlocked
    ///     category, and every unfinished job but the first in a locked one.
    /// </summary>
    public static HashSet<int> SkippedSlots(
        RelicLine line,
        RelicOwnership ownership,
        IReadOnlyDictionary<string, string>? firstJobs)
    {
        HashSet<int> skipped = [];
        if (!Applies(line))
        {
            return skipped;
        }

        foreach (var category in CategoryNames)
        {
            var unfinished = UnfinishedSlots(line, ownership, category);
            var first = IsCategoryUnlocked(line, ownership, category)
                ? -1
                : FirstSlot(line, ownership, category, firstJobs?.GetValueOrDefault(category));
            skipped.UnionWith(unfinished.Where(slot => slot != first));
        }

        return skipped;
    }

    /// <summary>Unfinished jobs in unlocked categories - they buy their Lucis tool from a Calamity Salvager.</summary>
    public static IReadOnlyList<int> SalvagerSlots(RelicLine line, RelicOwnership ownership)
    {
        List<int> result = [];
        foreach (var category in CategoryNames)
        {
            if (IsCategoryUnlocked(line, ownership, category))
            {
                result.AddRange(UnfinishedSlots(line, ownership, category));
            }
        }

        return result;
    }

    private static int StepsReached(RelicLine line, RelicOwnership ownership, int slot)
    {
        var reached = 0;
        for (var tier = 0; tier < line.TierCount; tier++)
        {
            if (ownership.IsStepDoneOrManual(line, slot, tier))
            {
                reached = tier + 1;
            }
        }

        return reached;
    }
}
