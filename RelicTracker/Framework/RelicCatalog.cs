using System.Text.Json;
using System.Text.Json.Serialization;

namespace RelicTracker.Framework;

/// <summary>
/// One relic "line" — a FFXIV Collect relic type (e.g. Anima Weapons) with its
/// ordered upgrade steps. Each step is a tier of <see cref="Jobs"/> relics.
/// </summary>
public sealed class RelicLine
{
    [JsonPropertyName("collectType")]
    public string CollectType { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("expansion")]
    public string Expansion { get; set; } = string.Empty;

    [JsonPropertyName("jobs")]
    public int Jobs { get; set; }

    [JsonPropertyName("tierCount")]
    public int TierCount { get; set; }

    [JsonPropertyName("relicCount")]
    public int RelicCount { get; set; }

    [JsonPropertyName("steps")]
    public List<string> Steps { get; set; } = [];

    [JsonPropertyName("jobList")]
    public List<string> JobList { get; set; } = [];

    [JsonPropertyName("slotRelics")]
    public List<string> SlotRelics { get; set; } = [];

    [JsonPropertyName("typeOrder")]
    public int TypeOrder { get; set; }

    /// <summary>Slot -> job order resolved from game data at runtime; empty until resolved.</summary>
    [JsonIgnore]
    public List<string> ResolvedJobs { get; set; } = [];

    /// <summary>Authoritative job order: resolved from the game when available, else the bundled list.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> EffectiveJobList =>
        Jobs > 0 && ResolvedJobs.Count == Jobs ? ResolvedJobs : JobList;

    [JsonIgnore]
    public string FinalStep => Steps.Count > 0 ? Steps[^1] : "Complete";

    public string StepName(int tierIndex) =>
        tierIndex >= 0 && tierIndex < Steps.Count ? Steps[tierIndex] : $"Step {tierIndex + 1}";
}

/// <summary>One stage of a relic armor set (a FFXIV Collect armor type).</summary>
public sealed class ArmorStage
{
    [JsonPropertyName("collectType")]
    public string CollectType { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("pieces")]
    public int Pieces { get; set; }
}

/// <summary>A field-operation relic armor line (Eurekan / Resistance / Phantom), per expansion.</summary>
public sealed class ArmorLine
{
    [JsonPropertyName("expansion")]
    public string Expansion { get; set; } = string.Empty;

    [JsonPropertyName("lineName")]
    public string LineName { get; set; } = string.Empty;

    [JsonPropertyName("stages")]
    public List<ArmorStage> Stages { get; set; } = [];

    [JsonIgnore]
    public int TotalPieces => Stages.Sum(stage => stage.Pieces);
}

/// <summary>
/// Loads the bundled relic catalog (relic_lines.json) — the canonical list of relic
/// lines and their steps, derived from the FFXIV Collect relic index.
/// </summary>
public sealed class RelicCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<RelicLine> Lines { get; private set; } = [];

    public IReadOnlyList<ArmorLine> ArmorLines { get; private set; } = [];

    public IReadOnlyList<string> Expansions { get; private set; } = [];

    private Dictionary<string, Dictionary<string, string>> stepNotes = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public void Load()
    {
        var baseDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");
        var path = Path.Combine(baseDir, "relic_lines.json");
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing relic catalog: {Path}", path);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            Lines = JsonSerializer.Deserialize<List<RelicLine>>(json, JsonOptions) ?? [];
            Expansions = Lines
                .Select(line => line.Expansion)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            IsLoaded = true;
            Svc.Log.Information("[RelicTracker] Loaded relic catalog: {LineCount} lines.", Lines.Count);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to load relic catalog from {Path}", path);
        }

        LoadStepNotes(Path.Combine(baseDir, "relic_step_notes.json"));
        LoadArmor(Path.Combine(baseDir, "relic_armor.json"));
    }

    private void LoadArmor(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            ArmorLines = JsonSerializer.Deserialize<List<ArmorLine>>(File.ReadAllText(path), JsonOptions) ?? [];
            Svc.Log.Information("[RelicTracker] Loaded {Count} relic armor lines.", ArmorLines.Count);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] Failed to load relic armor from {Path}", path);
        }
    }

    public IEnumerable<ArmorLine> ArmorLinesFor(string expansionId) =>
        ArmorLines.Where(line => string.Equals(line.Expansion, expansionId, StringComparison.Ordinal));

    private void LoadStepNotes(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path), JsonOptions);
            if (parsed is not null)
            {
                stepNotes = parsed;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] Failed to load relic step notes from {Path}", path);
        }
    }

    /// <summary>A curated "what to do" note for a step, falling back to the line-level note.</summary>
    public string? StepNote(string collectType, string stepName)
    {
        if (!stepNotes.TryGetValue(collectType, out var byStep))
        {
            return null;
        }

        if (byStep.TryGetValue(stepName, out var note) && !string.IsNullOrWhiteSpace(note))
        {
            return note;
        }

        return byStep.TryGetValue("_line", out var lineNote) && !string.IsNullOrWhiteSpace(lineNote) ? lineNote : null;
    }

    /// <summary>Resolves each line's slot -> job order from game data, falling back to the bundled list.</summary>
    public void ResolveJobs(ItemResolver items)
    {
        var resolvedLines = 0;
        foreach (var line in Lines)
        {
            if (line.Jobs <= 0 || line.SlotRelics.Count != line.Jobs)
            {
                continue;
            }

            var resolved = new List<string>(line.Jobs);
            foreach (var relicName in line.SlotRelics)
            {
                if (!items.TryResolveEquipJob(relicName, out var abbrev))
                {
                    resolved.Clear();
                    break;
                }

                resolved.Add(abbrev);
            }

            if (resolved.Count == line.Jobs)
            {
                line.ResolvedJobs = resolved;
                resolvedLines++;
            }
            else
            {
                Svc.Log.Warning(
                    "[RelicTracker] Could not resolve all jobs for {Line}; using bundled order.",
                    line.CollectType);
            }
        }

        Svc.Log.Information("[RelicTracker] Resolved job order from game data for {Count}/{Total} relic lines.", resolvedLines, Lines.Count);
    }

    public IEnumerable<RelicLine> LinesFor(string expansionId) =>
        Lines.Where(line => string.Equals(line.Expansion, expansionId, StringComparison.Ordinal));
}
