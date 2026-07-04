using Lumina.Excel.Sheets;
namespace RelicTracker.Framework;

/// <summary>Maps an item's ClassJobCategory to a single job abbreviation using Lumina game data.</summary>
internal static class ClassJobEquipResolver
{
    private static readonly HashSet<string> BaseClassAbbrevs = new(StringComparer.Ordinal)
    {
        "GLA", "MRD", "PGL", "LNC", "ARC", "CNJ", "THM"
    };

    private static readonly (string Abbrev, Func<ClassJobCategory, bool> Has)[] JobAccessors = BuildJobAccessors();

    public static bool TryResolve(ClassJobCategory category, out string jobAbbrev)
    {
        foreach ((var abbrev, var has) in JobAccessors)
        {
            if (!has(category))
            {
                continue;
            }

            jobAbbrev = abbrev;
            return true;
        }

        jobAbbrev = string.Empty;
        return false;
    }

    private static (string Abbrev, Func<ClassJobCategory, bool> Has)[] BuildJobAccessors()
    {
        string[] priority = [.. JobColumnDefaults.CombatJobs, .. JobColumnDefaults.DoHDoLJobs];
        var order = priority
            .Select((abbrev, index) => (abbrev, index))
            .ToDictionary(entry => entry.abbrev, entry => entry.index, StringComparer.Ordinal);

        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        List<(string Abbrev, Func<ClassJobCategory, bool> Has)> accessors = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var job in sheet)
        {
            var abbrev = job.Abbreviation.ToString().Trim();
            if (string.IsNullOrEmpty(abbrev) || BaseClassAbbrevs.Contains(abbrev) || !seen.Add(abbrev))
            {
                continue;
            }

            var property = typeof(ClassJobCategory).GetProperty(abbrev);
            if (property?.PropertyType != typeof(bool))
            {
                continue;
            }

            accessors.Add((abbrev, category => (bool)property.GetValue(category)!));
        }

        accessors.Sort((left, right) =>
            order.GetValueOrDefault(left.Abbrev, 999).CompareTo(order.GetValueOrDefault(right.Abbrev, 999)));

        return [.. accessors];
    }
}
