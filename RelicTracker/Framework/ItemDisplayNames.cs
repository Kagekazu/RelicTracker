using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

internal static class ItemDisplayNames
{
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
