using System.Text.Json;
using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

public sealed class ItemResolver
{
    private static readonly JsonSerializerOptions AliasJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, uint> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> aliasToNames = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        byName.Clear();
        aliasToNames.Clear();

        var sheet = Svc.Data.GetExcelSheet<Item>();
        foreach (var row in sheet)
        {
            var name = row.Name.ToString().Trim();
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

    public bool TryResolve(string materialName, out uint itemId)
    {
        var ids = ResolveItemIds(materialName);
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
        var ids = new List<uint>();
        foreach (var candidate in GetCandidateNames(materialName))
        {
            if (byName.TryGetValue(candidate, out var itemId) && ids.All(id => id != itemId))
            {
                ids.Add(itemId);
            }
        }

        return ids;
    }

    private IEnumerable<string> GetCandidateNames(string materialName)
    {
        var trimmed = materialName.Trim();
        if (aliasToNames.TryGetValue(trimmed, out var aliasedNames))
        {
            foreach (var aliasedName in aliasedNames)
            {
                yield return aliasedName;
            }

            yield break;
        }

        foreach (var candidate in ExpandNameVariants(trimmed))
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
        var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data", "material_aliases.json");
        if (!File.Exists(path))
        {
            Svc.Log.Warning("[RelicTracker] Missing material alias file: {Path}", path);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
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
            _ => [],
        };
    }
}
