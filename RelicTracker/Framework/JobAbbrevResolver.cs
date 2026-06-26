using Lumina.Excel;
using Lumina.Excel.Sheets;
namespace RelicTracker.Framework;

public sealed class JobAbbrevResolver
{
    private readonly Dictionary<string, string> abbrevByToken = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        abbrevByToken.Clear();

        ExcelSheet<ClassJob> sheet = Svc.Data.GetExcelSheet<ClassJob>();
        foreach(ClassJob row in sheet)
        {
            string abbrev = row.Abbreviation.ToString().Trim();
            string name = row.Name.ToString().Trim();
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

        foreach(string abbrev in JobColumnDefaults.CombatJobs)
        {
            abbrevByToken.TryAdd(abbrev, abbrev);
        }

        foreach(string abbrev in JobColumnDefaults.DoHDoLJobs)
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

        string trimmed = name.Trim();
        if (abbrevByToken.TryGetValue(trimmed, out string? abbrev))
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

        string[] source = string.Equals(expansionId, "DoHDoL", StringComparison.Ordinal)
            ? DoHDoLJobs
            : CombatJobs;

        List<string> names = new(jobCount);
        for(int index = 0; index < jobCount; index++)
        {
            names.Add(index < source.Length ? source[index] : $"J{index + 1}");
        }

        return names;
    }
}
