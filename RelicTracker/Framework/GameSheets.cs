using Dalamud.Game;
using Lumina.Excel;
namespace RelicTracker.Framework;

/// <summary>Excel sheet access pinned to English, independent of the game client's language.</summary>
internal static class GameSheets
{
    /// <summary>
    ///     The English sheet when the client ships English data, otherwise the client-language
    ///     sheet. Bundled relic data stores English names only, so all name-to-id matching must
    ///     run against English rows regardless of client language; only clients without English
    ///     data (CN/KR) fall back.
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
