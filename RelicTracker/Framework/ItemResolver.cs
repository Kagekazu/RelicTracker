using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

public sealed class ItemResolver
{
    private readonly Dictionary<string, uint> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        _byName.Clear();
        var sheet = Svc.Data.GetExcelSheet<Item>();
        foreach (var row in sheet)
        {
            var name = row.Name.ToString().Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            _byName.TryAdd(name, row.RowId);
            if (name.StartsWith("HQ ", StringComparison.OrdinalIgnoreCase))
            {
                _byName.TryAdd(name[3..].Trim(), row.RowId);
            }
        }

        Svc.Log.Information("[RelicTracker] Indexed {Count} item names for lookup.", _byName.Count);
    }

    public bool TryResolve(string materialName, out uint itemId)
    {
        itemId = 0;
        var name = materialName.Trim();
        if (_byName.TryGetValue(name, out itemId))
        {
            return true;
        }

        if (name.StartsWith("HQ ", StringComparison.OrdinalIgnoreCase) &&
            _byName.TryGetValue(name[3..].Trim(), out itemId))
        {
            return true;
        }

        return false;
    }
}
