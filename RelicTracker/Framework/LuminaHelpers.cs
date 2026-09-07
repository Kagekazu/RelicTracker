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

    public static T? ReadJson<T>(string fileName, JsonSerializerOptions options)
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
