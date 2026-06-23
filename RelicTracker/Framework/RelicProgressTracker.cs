namespace RelicTracker.Framework;

public sealed class ProgressRowDefinition
{
    public required string Step { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<bool?> Jobs { get; init; }
}

public sealed class RelicProgressTracker
{
    private readonly Configuration configuration;
    private readonly CollectProgressSync collectSync;

    public RelicProgressTracker(Configuration configuration, CollectProgressSync collectSync)
    {
        this.configuration = configuration;
        this.collectSync = collectSync;
    }

    public void EnsureCollectSynced(FfxivCollectService collectService, RelicDataService data)
    {
        var characterId = configuration.FfxivCollectCharacterId;
        if (characterId == 0)
        {
            return;
        }

        collectSync.EnsureSynced(characterId, collectService.Snapshot, collectService.LastRefreshUtc, data);
    }

    public bool UsesCollectProgress =>
        configuration.FfxivCollectCharacterId > 0 && collectSync.IsActive(configuration.FfxivCollectCharacterId);

    public bool UsesManualProgress => !UsesCollectProgress;

    public static string CellKey(string expansionId, string step, string label, int jobIndex) =>
        $"{expansionId}|{step}|{label}|{jobIndex}";

    public static bool IsApplicable(bool? sheetState) => sheetState == true;

    public bool IsComplete(string expansionId, string step, string label, int jobIndex, bool? sheetState)
    {
        if (!IsApplicable(sheetState))
        {
            return true;
        }

        if (UsesCollectProgress)
        {
            return collectSync.IsStepCompleteForJob(expansionId, step, jobIndex);
        }

        return IsManualComplete(expansionId, step, label, jobIndex, sheetState);
    }

    private bool IsManualComplete(string expansionId, string step, string label, int jobIndex, bool? sheetState)
    {
        var key = CellKey(expansionId, step, label, jobIndex);
        if (configuration.UncompletedProgress.Contains(key))
        {
            return false;
        }

        if (configuration.CompletedProgress.Contains(key))
        {
            return true;
        }

        return false;
    }

    public void SetComplete(string expansionId, string step, string label, int jobIndex, bool complete)
    {
        var key = CellKey(expansionId, step, label, jobIndex);
        if (complete)
        {
            configuration.CompletedProgress.Add(key);
            configuration.UncompletedProgress.Remove(key);
        }
        else
        {
            configuration.CompletedProgress.Remove(key);
            configuration.UncompletedProgress.Add(key);
        }

        configuration.OnSettingChanged();
    }

    public void ClearExpansion(string expansionId)
    {
        var prefix = expansionId + "|";
        configuration.CompletedProgress.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        configuration.UncompletedProgress.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        configuration.OnSettingChanged();
    }

    public void MarkExpansionComplete(string expansionId, ExpansionSheet sheet)
    {
        foreach (var row in GetProgressRows(sheet))
        {
            for (var jobIndex = 0; jobIndex < row.Jobs.Count; jobIndex++)
            {
                if (!IsApplicable(row.Jobs[jobIndex]))
                {
                    continue;
                }

                SetComplete(expansionId, row.Step, row.Label, jobIndex, true);
            }
        }
    }

    public bool IsComplete(string expansionId, string step, string label, int jobIndex, IReadOnlyList<bool?> jobs) =>
        jobIndex < jobs.Count && IsComplete(expansionId, step, label, jobIndex, jobs[jobIndex]);

    public int CountIncompleteJobs(
        string expansionId,
        IReadOnlyList<bool?> jobs,
        string step,
        string label)
    {
        var incomplete = 0;
        for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
        {
            if (!IsApplicable(jobs[jobIndex]))
            {
                continue;
            }

            if (!IsComplete(expansionId, step, label, jobIndex, jobs[jobIndex]))
            {
                incomplete++;
            }
        }

        return incomplete;
    }

    public uint CalculateNeeded(
        string expansionId,
        IReadOnlyList<bool?> jobs,
        string step,
        string label,
        double? perUnit,
        double? sheetRemaining)
    {
        if (!HasProgressCheckboxes(jobs))
        {
            return CalculateNeededWithoutJobs(expansionId, step, perUnit, sheetRemaining);
        }

        if (perUnit is null or <= 0)
        {
            return 0;
        }

        var incompleteJobs = CountIncompleteJobs(expansionId, jobs, step, label);
        return (uint)Math.Max(0, Math.Round(perUnit.Value * incompleteJobs));
    }

    public uint CalculateNeededWithoutJobs(
        string expansionId,
        string step,
        double? perUnit,
        double? sheetRemaining)
    {
        if (UsesCollectProgress && CollectStepMap.TryGetRequirement(expansionId, step, out _))
        {
            return 0;
        }

        if (sheetRemaining is >= 0)
        {
            return (uint)Math.Max(0, Math.Round(sheetRemaining.Value));
        }

        return perUnit is > 0 ? (uint)Math.Max(0, Math.Round(perUnit.Value)) : 0;
    }

    public static IReadOnlyList<ProgressRowDefinition> GetProgressRows(ExpansionSheet sheet)
    {
        var seen = new HashSet<(string Step, string Label)>();
        var rows = new List<ProgressRowDefinition>();

        void TryAdd(string? step, string? label, IReadOnlyList<bool?> jobs)
        {
            if (!HasProgressCheckboxes(jobs))
            {
                return;
            }

            var normalizedStep = step ?? string.Empty;
            var normalizedLabel = label ?? string.Empty;
            if (!seen.Add((normalizedStep, normalizedLabel)))
            {
                return;
            }

            rows.Add(new ProgressRowDefinition
            {
                Step = normalizedStep,
                Label = normalizedLabel,
                Jobs = jobs,
            });
        }

        foreach (var material in sheet.Materials)
        {
            TryAdd(material.Step, material.Label, material.Jobs);
        }

        foreach (var step in sheet.Steps)
        {
            if (string.Equals(step.Kind, "stepsRemaining", StringComparison.Ordinal))
            {
                continue;
            }

            TryAdd(step.Step, step.Label, step.Jobs);
        }

        return rows;
    }

    public static bool HasProgressCheckboxes(IReadOnlyList<bool?>? jobs) =>
        jobs?.Any(job => job == true) == true;

    public static IReadOnlyList<string> GetJobNames(
        ExpansionSheet sheet,
        JobAbbrevResolver? abbrevResolver = null,
        IReadOnlyDictionary<string, List<string>>? jobColumnsByExpansion = null)
    {
        IReadOnlyList<string> rawNames;
        if (jobColumnsByExpansion is not null
            && jobColumnsByExpansion.TryGetValue(sheet.Id, out var configuredColumns)
            && configuredColumns.Count > 0)
        {
            rawNames = PadJobNames(configuredColumns, configuredColumns.Count);
        }
        else if (sheet.JobNames.Count == sheet.JobCount && sheet.JobCount > 0)
        {
            rawNames = sheet.JobNames;
        }
        else
        {
            rawNames = JobColumnDefaults.GetForExpansion(sheet.Id, sheet.JobCount);
        }

        if (abbrevResolver is null)
        {
            return rawNames.Select(AbbreviateJobName).ToList();
        }

        return rawNames.Select(abbrevResolver.ToAbbreviation).ToList();
    }

    public static int GetActiveJobColumnCount(
        ExpansionSheet sheet,
        IReadOnlyList<ProgressRowDefinition> progressRows,
        IReadOnlyDictionary<string, List<string>>? jobColumnsByExpansion = null)
    {
        var maxColumns = sheet.JobCount;
        if (jobColumnsByExpansion is not null
            && jobColumnsByExpansion.TryGetValue(sheet.Id, out var configuredColumns)
            && configuredColumns.Count > 0)
        {
            maxColumns = configuredColumns.Count;
        }

        var activeColumns = 0;
        for (var jobIndex = 0; jobIndex < maxColumns; jobIndex++)
        {
            if (progressRows.Any(row => jobIndex < row.Jobs.Count && row.Jobs[jobIndex] == true))
            {
                activeColumns = jobIndex + 1;
            }
        }

        return activeColumns;
    }

    private static IReadOnlyList<string> PadJobNames(IReadOnlyList<string> configuredColumns, int jobCount)
    {
        if (jobCount <= 0)
        {
            return [];
        }

        var names = new List<string>(jobCount);
        for (var index = 0; index < jobCount; index++)
        {
            names.Add(index < configuredColumns.Count
                ? configuredColumns[index]
                : $"Job {index + 1}");
        }

        return names;
    }

    public static string ResolveJobDisplayName(
        IReadOnlyList<string> jobNames,
        int jobIndex,
        JobAbbrevResolver? abbrevResolver = null)
    {
        if (jobIndex < jobNames.Count)
        {
            return jobNames[jobIndex];
        }

        var fallback = $"Job {jobIndex + 1}";
        return abbrevResolver?.ToAbbreviation(fallback) ?? fallback;
    }

    public static string AbbreviateJobName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var trimmed = name.Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        if (trimmed.StartsWith("Job ", StringComparison.Ordinal))
        {
            return trimmed[4..];
        }

        return trimmed[..Math.Min(4, trimmed.Length)];
    }
}
