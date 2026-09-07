using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

internal static class ClassJobEquipResolver
{
    private static readonly HashSet<string> BaseClassAbbrevs = new(StringComparer.Ordinal)
    {
        "GLA", "MRD", "PGL", "LNC", "ARC", "CNJ", "THM"
    };

    private static (string Abbrev, Func<ClassJobCategory, bool> Has)[]? jobAccessors;

    public static bool TryResolveEquipJobByItemId(uint itemId, out string jobAbbrev)
    {
        jobAbbrev = string.Empty;
        if (itemId == 0)
        {
            return false;
        }

        var item = GameSheets.English<Item>().GetRowOrDefault(itemId);
        if (item is null)
        {
            return false;
        }

        var category = item.Value.ClassJobCategory.ValueNullable;
        if (category is null)
        {
            return false;
        }

        return TryResolve(category.Value, out jobAbbrev);
    }

    private static bool TryResolve(ClassJobCategory category, out string jobAbbrev)
    {
        foreach ((string abbrev, Func<ClassJobCategory, bool> has) in Accessors())
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

    private static (string Abbrev, Func<ClassJobCategory, bool> Has)[] Accessors() =>
        jobAccessors ??= BuildJobAccessors();

    private static (string Abbrev, Func<ClassJobCategory, bool> Has)[] BuildJobAccessors()
    {
        string[] priority = [.. JobColumnDefaults.CombatJobs, .. JobColumnDefaults.DoHDoLJobs];
        Dictionary<string, int> order = priority
            .Select((abbrev, index) => (abbrev, index))
            .ToDictionary(entry => entry.abbrev, entry => entry.index, StringComparer.Ordinal);

        var sheet = GameSheets.English<ClassJob>();
        List<(string Abbrev, Func<ClassJobCategory, bool> Has)> accessors = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ClassJob job in sheet)
        {
            string abbrev = job.Abbreviation.ToString().Trim();
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
}
