using Dalamud.Game;
using Lumina.Excel;
namespace RelicTracker.Framework;

/// <summary>Excel sheet access pinned to English — bundled data stores English names only.</summary>
internal static class GameSheets
{
    /// <summary>
    ///     English rows for DE/FR/JA clients. CN/KR clients without English data fall back to the
    ///     client-language sheet (name matching may still fail there).
    /// </summary>
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
