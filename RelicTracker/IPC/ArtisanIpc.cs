using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

internal static class ArtisanIpc
{
    private const long BindRetryMs = 60_000;

    private static ICallGateSubscriber<string, int, int>? _getRelicToolListId;
    private static ICallGateSubscriber<int, object>? _startListById;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static bool _ipcBound;
    private static bool _loggedBindFailure;
    private static long _lastBindAttemptTick;

    public static bool IsInstalled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out _, false, true)
        || Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "Artisan");

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out _, false, true);

    /// <summary>True once Artisan exposes <c>Artisan.GetRelicToolListId</c> (relic premade lists build).</summary>
    public static bool SupportsRelicToolLists
    {
        get
        {
            EnsureBound();
            return _ipcBound;
        }
    }

    public static bool IsReady => SupportsRelicToolLists;

    public static void Init() => EnsureBound();

    public static void Dispose()
    {
        _getRelicToolListId = null;
        _startListById = null;
        _isBusy = null;
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
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
        if (!TryGetRelicToolListId(stepName, craftSlot, out int listId))
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

        long now = Environment.TickCount64;
        if (_lastBindAttemptTick != 0 && now - _lastBindAttemptTick < BindRetryMs)
        {
            return;
        }

        _lastBindAttemptTick = now;

        try
        {
            _getRelicToolListId ??= Svc.PluginInterface.GetIpcSubscriber<string, int, int>("Artisan.GetRelicToolListId");
            _startListById ??= Svc.PluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");
            _isBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");

            if (!_getRelicToolListId.HasFunction)
            {
                return;
            }

            _ = _getRelicToolListId.InvokeFunc(string.Empty, 0);
            _ipcBound = true;
            _loggedBindFailure = false;
            Svc.Log.Information("[RelicTracker] Artisan IPC ready.");
        }
        catch (Exception ex)
        {
            if (!_loggedBindFailure)
            {
                Svc.Log.Debug(ex, "[RelicTracker] Artisan IPC not available yet.");
                _loggedBindFailure = true;
            }
        }
    }
}
