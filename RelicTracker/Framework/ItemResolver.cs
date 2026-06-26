using Lumina.Excel;
using Lumina.Excel.Sheets;
namespace RelicTracker.Framework;

public sealed class ItemResolver
{
    // Job bool accessors on ClassJobCategory, jobs only (no base classes), in the order
    // we prefer to report. A relic weapon/tool is equippable by exactly one of these.
    private static readonly (string Abbrev, Func<ClassJobCategory, bool> Has)[] JobAccessors =
    [
        ("PLD", c => c.PLD), ("WAR", c => c.WAR), ("DRK", c => c.DRK), ("GNB", c => c.GNB),
        ("WHM", c => c.WHM), ("SCH", c => c.SCH), ("AST", c => c.AST), ("SGE", c => c.SGE),
        ("MNK", c => c.MNK), ("DRG", c => c.DRG), ("NIN", c => c.NIN), ("SAM", c => c.SAM),
        ("RPR", c => c.RPR), ("VPR", c => c.VPR),
        ("BRD", c => c.BRD), ("MCH", c => c.MCH), ("DNC", c => c.DNC),
        ("BLM", c => c.BLM), ("SMN", c => c.SMN), ("RDM", c => c.RDM), ("PCT", c => c.PCT),
        ("BLU", c => c.BLU),
        ("CRP", c => c.CRP), ("BSM", c => c.BSM), ("ARM", c => c.ARM), ("GSM", c => c.GSM),
        ("LTW", c => c.LTW), ("WVR", c => c.WVR), ("ALC", c => c.ALC), ("CUL", c => c.CUL),
        ("MIN", c => c.MIN), ("BTN", c => c.BTN), ("FSH", c => c.FSH)
    ];
    private readonly Dictionary<string, IReadOnlyList<string>> aliasToNames = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, uint> byName = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        byName.Clear();
        aliasToNames.Clear();

        ExcelSheet<Item> sheet = Svc.Data.GetExcelSheet<Item>();
        foreach (Item row in sheet)
        {
            string name = row.Name.ToString().Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            byName.TryAdd(name, row.RowId);
            if (name.StartsWith("HQ ", StringComparison.OrdinalIgnoreCase))
            {
                byName.TryAdd(name[3..].Trim(), row.RowId);
            }
        }

        LoadAliases();

        Svc.Log.Information(
            "[RelicTracker] Indexed {ItemCount} item names and {AliasCount} material aliases.",
            byName.Count,
            aliasToNames.Count);
    }

    /// <summary>Resolves the single job a relic weapon/tool is equippable by, from its item name.</summary>
    public bool TryResolveEquipJob(string itemName, out string jobAbbrev)
    {
        jobAbbrev = string.Empty;
        if (!byName.TryGetValue(itemName.Trim(), out uint rowId))
        {
            return false;
        }

        Item? item = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(rowId);
        if (item is null)
        {
            return false;
        }

        ClassJobCategory? category = item.Value.ClassJobCategory.ValueNullable;
        if (category is null)
        {
            return false;
        }

        foreach ((string abbrev, Func<ClassJobCategory, bool> has) in JobAccessors)
        {
            if (has(category.Value))
            {
                jobAbbrev = abbrev;
                return true;
            }
        }

        return false;
    }

    public bool TryResolve(string materialName, out uint itemId)
    {
        IReadOnlyList<uint> ids = ResolveItemIds(materialName);
        if (ids.Count == 0)
        {
            itemId = 0;
            return false;
        }

        itemId = ids[0];
        return true;
    }

    public IReadOnlyList<uint> ResolveItemIds(string materialName)
    {
        List<uint> ids = [];
        foreach (string candidate in GetCandidateNames(materialName))
        {
            if (byName.TryGetValue(candidate, out uint itemId) && ids.All(id => id != itemId))
            {
                ids.Add(itemId);
            }
        }

        return ids;
    }

    private IEnumerable<string> GetCandidateNames(string materialName)
    {
        string trimmed = materialName.Trim();
        if (aliasToNames.TryGetValue(trimmed, out IReadOnlyList<string>? aliasedNames))
        {
            foreach (string aliasedName in aliasedNames)
            {
                yield return aliasedName;
            }

            yield break;
        }

        foreach (string candidate in ExpandNameVariants(trimmed))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> ExpandNameVariants(string name)
    {
        yield return name;

        if (name.StartsWith("HQ ", StringComparison.OrdinalIgnoreCase))
        {
            yield return name[3..].Trim();
        }

        if (name.EndsWith(" Parts", StringComparison.Ordinal))
        {
            yield return name[..^1];
            yield return name.Replace(" Parts", " Component", StringComparison.Ordinal);
            yield return name.Replace(" Parts", " Components", StringComparison.Ordinal);
        }

        if (name.EndsWith(" Pars", StringComparison.Ordinal))
        {
            yield return name.Replace(" Pars", " Part", StringComparison.Ordinal);
            yield return name.Replace(" Pars", " Parts", StringComparison.Ordinal);
        }

        if (name.EndsWith(" parts", StringComparison.Ordinal))
        {
            yield return name[..^1];
        }
    }

    private void LoadAliases()
    {
        string path = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data", "material_aliases.json");
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing material alias file: {Path}", path);
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                aliasToNames[property.Name] = ParseAliasNames(property.Value);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[RelicTracker] Failed to load material aliases from {Path}", path);
        }
    }

    private static IReadOnlyList<string> ParseAliasNames(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString() ?? string.Empty],
            JsonValueKind.Array => value.EnumerateArray()
                .Select(element => element.GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList(),
            var _ => []
        };
    }
}
