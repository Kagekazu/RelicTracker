namespace RelicTracker.Framework;

internal static class MaterialAggregator
{
    public static List<MaterialDisplayRow> Aggregate(IEnumerable<MaterialDisplayRow> rows)
    {
        var aggregated = new Dictionary<string, MaterialDisplayRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = $"{row.Section}|{row.Name}|{row.IsCurrency}";
            if (!aggregated.TryGetValue(key, out var existing))
            {
                aggregated[key] = row;
                continue;
            }

            aggregated[key] = Merge(existing, row);
        }

        return aggregated.Values.ToList();
    }

    private static MaterialDisplayRow Merge(MaterialDisplayRow left, MaterialDisplayRow right)
    {
        var steps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddStep(steps, left);
        AddStep(steps, right);

        var jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddJobs(jobs, left.JobsNeeded);
        AddJobs(jobs, right.JobsNeeded);

        return new MaterialDisplayRow
        {
            ExpansionId = left.ExpansionId,
            Section = left.Section,
            Step = steps.Count == 1 ? steps.First() : null,
            Label = steps.Count == 1 ? left.Label ?? right.Label : null,
            DisplayStepOverride = steps.Count switch
            {
                0 => "—",
                1 => FormatStepLabel(steps.First(), left.Label ?? right.Label),
                _ => "Various",
            },
            Name = left.Name,
            ItemId = left.ItemId ?? right.ItemId,
            ItemIds = left.ItemIds.Count > 0 ? left.ItemIds : right.ItemIds,
            Needed = left.Needed + right.Needed,
            Owned = Math.Max(left.Owned, right.Owned),
            IsCurrency = left.IsCurrency,
            IsCurrencyTracked = left.IsCurrencyTracked || right.IsCurrencyTracked,
            JobsNeeded = jobs.Count == 0 ? null : string.Join(", ", jobs.OrderBy(job => job, StringComparer.OrdinalIgnoreCase)),
        };
    }

    private static void AddStep(HashSet<string> steps, MaterialDisplayRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Step))
        {
            steps.Add(row.Step);
        }
    }

    private static void AddJobs(HashSet<string> jobs, string? jobsNeeded)
    {
        if (string.IsNullOrWhiteSpace(jobsNeeded) || jobsNeeded == "—")
        {
            return;
        }

        foreach (var job in jobsNeeded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            jobs.Add(job);
        }
    }

    private static string FormatStepLabel(string step, string? label) =>
        string.IsNullOrWhiteSpace(label) ? step : $"{step} — {label}";
}
