using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
namespace RelicTracker.IPC;

internal static class ArtisanIpc
{
    private static ICallGateSubscriber<string, int, int>? _getRelicToolListId;
    private static ICallGateSubscriber<int, object>? _startListById;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static bool _ipcBound;

    public static bool IsInstalled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out var _, false, true)
        || Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "Artisan");

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out var _, false, true);

    public static bool IsReady
    {
        get
        {
            EnsureBound();
            return _ipcBound;
        }
    }

    public static void Init() => EnsureBound();

    public static void Dispose()
    {
        _getRelicToolListId = null;
        _startListById = null;
        _isBusy = null;
        _ipcBound = false;
    }

    public static bool TryGetRelicToolListId(string stepName, int craftSlot, out int listId)
    {
        listId = 0;
        if (!IsReady || _getRelicToolListId == null)
        {
            return false;
        }

        try
        {
            listId = _getRelicToolListId.InvokeFunc(stepName, craftSlot);
            return listId != 0;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan.GetRelicToolListId failed for {Step} slot {Slot}", stepName, craftSlot);
            return false;
        }
    }

    public static bool TryStartRelicToolList(string stepName, int craftSlot, out string? error)
    {
        error = null;
        if (!TryGetRelicToolListId(stepName, craftSlot, out var listId))
        {
            error = "No Artisan premade list for this step.";
            return false;
        }

        if (IsBusy())
        {
            error = "Artisan is already crafting.";
            return false;
        }

        if (_startListById == null)
        {
            error = "Artisan IPC is not ready.";
            return false;
        }

        try
        {
            _startListById.InvokeAction(listId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool IsBusy()
    {
        if (!IsReady || _isBusy == null)
        {
            return false;
        }

        try
        {
            return _isBusy.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan.IsBusy failed.");
            return false;
        }
    }

    private static void EnsureBound()
    {
        if (_ipcBound || !IsEnabled)
        {
            return;
        }

        try
        {
            _getRelicToolListId = Svc.PluginInterface.GetIpcSubscriber<string, int, int>("Artisan.GetRelicToolListId");
            _startListById = Svc.PluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");
            _isBusy = Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
            _ = _getRelicToolListId.InvokeFunc(string.Empty, 0);
            _ipcBound = true;
            Svc.Log.Information("[RelicTracker] Artisan IPC ready.");
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan IPC not available yet.");
        }
    }
}
