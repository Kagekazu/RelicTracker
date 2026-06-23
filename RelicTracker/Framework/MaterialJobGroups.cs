namespace RelicTracker.Framework;

/// <summary>
/// Wyn groups some materials under Crafters / Miner &amp; Botanist / Fisher headers without per-job checkboxes.
/// </summary>
internal static class MaterialJobGroups
{
    private static readonly HashSet<string> SubsectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Crafters",
        "Fisher",
        "Miner & Botanist",
    };

    public static bool IsSubsectionHeader(string? material) =>
        !string.IsNullOrWhiteSpace(material) && SubsectionHeaders.Contains(material.Trim());

    public static IReadOnlyList<bool?> ApplySubsectionJobs(
        string? subsection,
        int jobCount,
        IReadOnlyList<bool?> existing)
    {
        if (existing.Any(job => job is not null) || string.IsNullOrWhiteSpace(subsection))
        {
            return existing;
        }

        var jobs = new bool?[jobCount];
        var (start, end) = subsection.Trim() switch
        {
            "Crafters" => (0, 7),
            "Miner & Botanist" => (8, 9),
            "Fisher" => (10, 10),
            _ => (-1, -1),
        };

        if (start < 0)
        {
            return existing;
        }

        for (var jobIndex = 0; jobIndex < jobCount; jobIndex++)
        {
            jobs[jobIndex] = jobIndex >= start && jobIndex <= end;
        }

        return jobs;
    }
}
