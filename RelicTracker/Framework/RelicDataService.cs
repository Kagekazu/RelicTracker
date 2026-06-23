using System.Text.Json;

namespace RelicTracker.Framework;

public sealed class RelicDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleDoubleJsonConverter() },
    };

    public RelicManifest Manifest { get; private set; } = new();
    public List<MaterialReferenceRow> MaterialReference { get; private set; } = [];
    public Dictionary<string, ExpansionSheet> Expansions { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> JobColumnsByExpansion { get; private set; } = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public void Load()
    {
        var baseDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");
        Manifest = ReadJson<RelicManifest>(Path.Combine(baseDir, "manifest.json")) ?? new RelicManifest();
        Expansions = ReadJson<Dictionary<string, ExpansionSheet>>(Path.Combine(baseDir, "expansions.json"))
                     ?? new Dictionary<string, ExpansionSheet>(StringComparer.Ordinal);
        MaterialReference = ReadJson<List<MaterialReferenceRow>>(Path.Combine(baseDir, "materials.json")) ?? [];
        JobColumnsByExpansion = ReadJson<Dictionary<string, List<string>>>(Path.Combine(baseDir, "job_columns.json"))
                                ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        TagMaterialReferenceExpansions();
        IsLoaded = true;
        Svc.Log.Information(
            "[RelicTracker] Loaded data {Version} ({Patch}) with {ExpansionCount} expansions.",
            Manifest.SheetVersion,
            Manifest.Patch,
            Expansions.Count);
    }

    public IEnumerable<MaterialDisplayRow> GetExpansionMaterials(
        string expansionId,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        RelicProgressTracker progress)
    {
        if (!Expansions.TryGetValue(expansionId, out var sheet))
        {
            yield break;
        }

        foreach (var row in MaterialAggregator.Aggregate(BuildRawMaterialRows(expansionId, sheet, items, ownedLookup, progress)))
        {
            yield return row;
        }
    }

    private IEnumerable<MaterialDisplayRow> BuildRawMaterialRows(
        string expansionId,
        ExpansionSheet sheet,
        ItemResolver items,
        Func<uint, uint> ownedLookup,
        RelicProgressTracker progress)
    {
        var jobNames = RelicProgressTracker.GetJobNames(sheet, null, JobColumnsByExpansion);
        string? materialSubsection = null;
        string? currentStep = null;
        var currencyTotals = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.Materials)
        {
            if (!string.IsNullOrWhiteSpace(row.Step) && !string.Equals(row.Step, currentStep, StringComparison.Ordinal))
            {
                currentStep = row.Step;
                materialSubsection = null;
            }

            var name = row.Material?.Trim();
            if (MaterialJobGroups.IsSubsectionHeader(name))
            {
                materialSubsection = name;
                continue;
            }

            if (!MaterialFilters.IsTrackableMaterial(name))
            {
                continue;
            }

            var jobs = MaterialJobGroups.ApplySubsectionJobs(materialSubsection, sheet.JobCount, row.Jobs);

            var itemIds = items.ResolveItemIds(name!);
            var owned = itemIds.Aggregate(0u, (total, itemId) => total + ownedLookup(itemId));

            var needed = RelicProgressTracker.HasProgressCheckboxes(jobs)
                ? progress.CalculateNeeded(
                    expansionId,
                    jobs,
                    row.Step ?? string.Empty,
                    row.Label ?? string.Empty,
                    row.PerUnit,
                    row.Remaining)
                : progress.CalculateNeededWithoutJobs(
                    expansionId,
                    row.Step ?? string.Empty,
                    row.PerUnit,
                    row.Remaining);

            yield return new MaterialDisplayRow
            {
                ExpansionId = expansionId,
                Section = CollectStepMap.ResolveSection(expansionId, row.Step, isCurrency: false),
                Step = row.Step,
                Label = row.Label,
                Name = name!,
                ItemId = itemIds.Count == 1 ? itemIds[0] : null,
                ItemIds = itemIds,
                Needed = needed,
                Owned = owned,
                IsCurrency = false,
                IsCurrencyTracked = false,
                JobsNeeded = FormatJobsNeeded(
                    expansionId,
                    jobs,
                    row.Step ?? string.Empty,
                    row.Label ?? string.Empty,
                    progress,
                    jobNames),
            };
        }

        foreach (var row in sheet.Currencies)
        {
            var name = row.Name?.Trim();
            if (!MaterialFilters.IsTrackableCurrency(name))
            {
                continue;
            }

            var perUnit = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            currencyTotals.TryGetValue(name!, out var existingTotal);
            currencyTotals[name!] = existingTotal + perUnit;
        }

        foreach (var (name, totalPerUnit) in currencyTotals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var needed = CurrencyBalances.CalculateNeeded(expansionId, totalPerUnit, sheet, progress);
            var owned = CurrencyBalances.GetOwned(name, items, ownedLookup);
            var tracked = CurrencyBalances.IsTrackable(name);

            yield return new MaterialDisplayRow
            {
                ExpansionId = expansionId,
                Section = CollectStepMap.ResolveSection(expansionId, null, isCurrency: true),
                Step = null,
                Label = null,
                Name = name,
                ItemId = null,
                ItemIds = [],
                Needed = needed,
                Owned = owned,
                IsCurrency = true,
                IsCurrencyTracked = tracked,
            };
        }
    }

    private static string? FormatJobsNeeded(
        string expansionId,
        IReadOnlyList<bool?> jobs,
        string step,
        string label,
        RelicProgressTracker progress,
        IReadOnlyList<string> jobNames)
    {
        if (!RelicProgressTracker.HasProgressCheckboxes(jobs))
        {
            return null;
        }

        var neededJobs = new List<string>();
        for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
        {
            if (!RelicProgressTracker.IsApplicable(jobs[jobIndex]))
            {
                continue;
            }

            if (!progress.IsComplete(expansionId, step, label, jobIndex, jobs[jobIndex]))
            {
                neededJobs.Add(RelicProgressTracker.ResolveJobDisplayName(jobNames, jobIndex));
            }
        }

        if (neededJobs.Count == 0)
        {
            return "—";
        }

        return string.Join(", ", neededJobs);
    }

    public IEnumerable<MaterialReferenceRow> GetMaterialReference(string expansionId)
    {
        return MaterialReference.Where(row =>
            !row.IsSectionHeader
            && string.Equals(row.Expansion, expansionId, StringComparison.Ordinal)
            && (!string.IsNullOrWhiteSpace(row.Material) || !string.IsNullOrWhiteSpace(row.Location)));
    }

    private void TagMaterialReferenceExpansions()
    {
        if (MaterialReference.Count == 0)
        {
            return;
        }

        if (MaterialReference.Any(row => !string.IsNullOrEmpty(row.Expansion)))
        {
            return;
        }

        var dohSteps = Expansions.TryGetValue("DoHDoL", out var dohSheet)
            ? dohSheet.Materials
                .Select(row => row.Step)
                .Where(step => !string.IsNullOrEmpty(step))
                .Select(step => step!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var headerIndices = MaterialReference
            .Select((row, index) => (row, index))
            .Where(pair => pair.row.IsSectionHeader)
            .Select(pair => pair.index)
            .ToList();

        var sectionBounds = new List<int> { 0 };
        sectionBounds.AddRange(headerIndices.Select(index => index + 1));
        sectionBounds.Add(MaterialReference.Count);
        sectionBounds = sectionBounds.Distinct().OrderBy(index => index).ToList();

        var sectionExpansions = new[] { "ARR", "HW", "SB", "ShB", "EW", "DT", "DoHDoL" };
        for (var sectionIndex = 0; sectionIndex < sectionExpansions.Length && sectionIndex + 1 < sectionBounds.Count; sectionIndex++)
        {
            var start = sectionBounds[sectionIndex];
            var end = sectionBounds[sectionIndex + 1];
            var sectionExpansion = sectionExpansions[sectionIndex];

            for (var rowIndex = start; rowIndex < end; rowIndex++)
            {
                var row = MaterialReference[rowIndex];
                if (row.IsSectionHeader)
                {
                    continue;
                }

                var step = row.Step;
                row.Expansion = sectionExpansion == "DT"
                    && !string.IsNullOrEmpty(step)
                    && dohSteps.Contains(step)
                    ? "DoHDoL"
                    : sectionExpansion;
            }
        }
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing data file: {Path}", path);
            return default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to read {Path}", path);
            return default;
        }
    }
}