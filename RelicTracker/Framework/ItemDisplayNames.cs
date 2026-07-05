using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

/// <summary>Client-language item labels for UI; bundled English names remain the lookup keys.</summary>
internal static class ItemDisplayNames
{
    /// <summary>Localized name when one item row backs the entry; bundled label for alias groups (e.g. Atma).</summary>
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
