using Lumina.Excel.Sheets;
namespace RelicTracker.Framework;

public sealed class ItemResolver
{
    private readonly Dictionary<string, IReadOnlyList<string>> aliasToNames = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, uint> byName = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<uint>> replicaIdsByRelicName = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        byName.Clear();
        aliasToNames.Clear();
        replicaIdsByRelicName.Clear();

        // English sheet: bundled relic/armor/material data stores English item names.
        var sheet = GameSheets.English<Item>();
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

            if (name.StartsWith("Replica ", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = name["Replica ".Length..].Trim();
                if (!string.IsNullOrEmpty(baseName))
                {
                    if (!replicaIdsByRelicName.TryGetValue(baseName, out List<uint>? replicas))
                    {
                        replicas = [];
                        replicaIdsByRelicName[baseName] = replicas;
                    }

                    if (!replicas.Contains(row.RowId))
                    {
                        replicas.Add(row.RowId);
                    }
                }
            }
        }

        LoadAliases();

        Svc.Log.Information(
            "[RelicTracker] Indexed {ItemCount} item names, {AliasCount} material aliases, {ReplicaCount} relic replica names.",
            byName.Count,
            aliasToNames.Count,
            replicaIdsByRelicName.Count);
    }

    /// <summary>Resolves the single job a relic weapon/tool is equippable by, from its item name.</summary>
    public bool TryResolveEquipJob(string itemName, out string jobAbbrev)
    {
        jobAbbrev = string.Empty;
        if (!byName.TryGetValue(itemName.Trim(), out var rowId))
        {
            return false;
        }

        var item = GameSheets.English<Item>().GetRowOrDefault(rowId);
        if (item is null)
        {
            return false;
        }

        var category = item.Value.ClassJobCategory.ValueNullable;
        if (category is null)
        {
            return false;
        }

        return ClassJobEquipResolver.TryResolve(category.Value, out jobAbbrev);
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

    public bool TryResolveItem(string itemName, out uint itemId) =>
        byName.TryGetValue(itemName.Trim(), out itemId);

    public IReadOnlyList<uint> GetReplicaIds(string relicName)
    {
        if (replicaIdsByRelicName.TryGetValue(relicName.Trim(), out List<uint>? replicas))
        {
            return replicas;
        }

        return [];
    }

    /// <summary>True if Allagan Tools reports any owned copy of the named item.</summary>
    public bool IsItemOwned(string itemName, Func<uint, uint> ownedLookup) =>
        TryResolveItem(itemName, out uint itemId) && ownedLookup(itemId) > 0;

    /// <summary>True if Allagan Tools reports any owned copy of the relic item or a vendor replica with the same base name.</summary>
    public bool IsRelicOrReplicaOwned(string relicName, Func<uint, uint> ownedLookup)
    {
        if (TryResolveItem(relicName, out uint relicId) && ownedLookup(relicId) > 0)
        {
            return true;
        }

        foreach (uint replicaId in GetReplicaIds(relicName))
        {
            if (ownedLookup(replicaId) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<uint> ResolveItemIds(string materialName)
    {
        List<uint> ids = [];
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

    private static IReadOnlyList<string> ParseAliasNames(JsonElement value) =>
        value.ValueKind switch
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
