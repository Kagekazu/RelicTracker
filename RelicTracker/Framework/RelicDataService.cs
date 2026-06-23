using System.Text.Json;

namespace RelicTracker.Framework;

public sealed class RelicDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public RelicManifest Manifest { get; private set; } = new();
    public List<MaterialReferenceRow> MaterialReference { get; private set; } = [];
    public Dictionary<string, ExpansionSheet> Expansions { get; private set; } = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public void Load()
    {
        var baseDir = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName ?? ".", "Data");
        Manifest = ReadJson<RelicManifest>(Path.Combine(baseDir, "manifest.json")) ?? new RelicManifest();
        MaterialReference = ReadJson<List<MaterialReferenceRow>>(Path.Combine(baseDir, "materials.json")) ?? [];
        Expansions = ReadJson<Dictionary<string, ExpansionSheet>>(Path.Combine(baseDir, "expansions.json"))
                     ?? new Dictionary<string, ExpansionSheet>(StringComparer.Ordinal);
        IsLoaded = true;
        Svc.Log.Information(
            "[RelicTracker] Loaded data {Version} ({Patch}) with {ExpansionCount} expansions.",
            Manifest.SheetVersion,
            Manifest.Patch,
            Expansions.Count);
    }

    public IEnumerable<MaterialDisplayRow> GetExpansionMaterials(string expansionId, ItemResolver items, Func<uint, uint> ownedLookup)
    {
        if (!Expansions.TryGetValue(expansionId, out var sheet))
        {
            yield break;
        }

        foreach (var row in sheet.Materials)
        {
            var name = row.Material?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var needed = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            var itemId = items.TryResolve(name, out var id) ? id : (uint?)null;
            var owned = itemId is uint itemIdValue ? ownedLookup(itemIdValue) : 0u;

            yield return new MaterialDisplayRow
            {
                ExpansionId = expansionId,
                Step = row.Step,
                Name = name,
                ItemId = itemId,
                Needed = needed,
                Owned = owned,
                IsCurrency = false,
            };
        }

        foreach (var row in sheet.Currencies)
        {
            var name = row.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            yield return new MaterialDisplayRow
            {
                ExpansionId = expansionId,
                Step = null,
                Name = name,
                ItemId = null,
                Needed = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0)),
                Owned = 0,
                IsCurrency = true,
            };
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
