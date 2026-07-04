using Lumina.Excel.Sheets;
namespace RelicTracker.Framework;

public sealed class JobAbbrevResolver
{
    private readonly Dictionary<string, string> abbrevByToken = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        abbrevByToken.Clear();

        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        foreach (var row in sheet)
        {
            var abbrev = row.Abbreviation.ToString().Trim();
            var name = row.Name.ToString().Trim();
            if (string.IsNullOrEmpty(abbrev))
            {
                continue;
            }

            abbrevByToken.TryAdd(abbrev, abbrev);
            if (!string.IsNullOrEmpty(name))
            {
                abbrevByToken.TryAdd(name, abbrev);
            }
        }

        foreach (var abbrev in JobColumnDefaults.CombatJobs)
        {
            abbrevByToken.TryAdd(abbrev, abbrev);
        }

        foreach (var abbrev in JobColumnDefaults.DoHDoLJobs)
        {
            abbrevByToken.TryAdd(abbrev, abbrev);
        }

        Svc.Log.Information("[RelicTracker] Indexed {Count} job abbreviations.", abbrevByToken.Count);
    }

    public string ToAbbreviation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var trimmed = name.Trim();
        if (abbrevByToken.TryGetValue(trimmed, out var abbrev))
        {
            return abbrev;
        }

        if (trimmed.StartsWith("Job ", StringComparison.Ordinal) && trimmed.Length > 4)
        {
            return trimmed[4..];
        }

        if (trimmed.Length <= 4)
        {
            return trimmed.ToUpperInvariant();
        }

        return trimmed[..Math.Min(3, trimmed.Length)].ToUpperInvariant();
    }
}

internal static class JobColumnDefaults
{
    public static readonly string[] CombatJobs =
    [
        "PLD", "MNK", "WAR", "DRG", "BRD", "BLM", "WHM", "SCH", "NIN", "DRK",
        "AST", "MCH", "SAM", "RDM", "GNB", "DNC", "VPR", "PCT"
    ];

    public static readonly string[] DoHDoLJobs =
    [
        "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH"
    ];

    public static IReadOnlyList<string> GetForExpansion(string expansionId, int jobCount)
    {
        if (jobCount <= 0)
        {
            return [];
        }

        var source = string.Equals(expansionId, "DoHDoL", StringComparison.Ordinal)
            ? DoHDoLJobs
            : CombatJobs;

        List<string> names = new(jobCount);
        for (var index = 0; index < jobCount; index++)
        {
            names.Add(index < source.Length ? source[index] : $"J{index + 1}");
        }

        return names;
    }
}
