using Dalamud.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

internal static class GameSheets
{
    public static ExcelSheet<T> English<T>() where T : struct, IExcelRow<T>
    {
        try
        {
            return Svc.Data.GetExcelSheet<T>(ClientLanguage.English);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] English {Sheet} sheet unavailable; falling back to client language.", typeof(T).Name);
            return Svc.Data.GetExcelSheet<T>();
        }
    }
}

internal static class ItemDisplayNames
{
    /// <summary>Client-language item name for UI; English sheets are used elsewhere for matching.</summary>
    public static string Label(IReadOnlyList<uint> itemIds, string bundledName)
    {
        if (itemIds.Count != 1)
        {
            return bundledName;
        }

        return Resolve(itemIds[0], bundledName);
    }

    public static string Resolve(uint itemId, string fallback)
    {
        if (itemId == 0)
        {
            return fallback;
        }

        var item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
        if (item is null)
        {
            return fallback;
        }

        var name = item.Value.Name.ToString().Trim();
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}

internal static class BundledData
{
    public static string DirectoryPath =>
        Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");

    public static T? ReadJson<T>(string fileName, JsonSerializerOptions? options = null)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing data file: {Path}", path);
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to read {Path}", path);
            return default;
        }
    }
}

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
        return category is not null && TryResolve(category.Value, out jobAbbrev);
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
