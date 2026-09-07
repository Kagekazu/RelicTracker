using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

internal static class ArtisanIpc
{
    private const long BindRetryMs = 60_000;

    /// <summary>
    ///     Matches Artisan <c>RelicToolPremadeLists.RelicToolStep</c> ordinals (1–11).
    ///     IPC is <c>Artisan.GetRelicToolListId(int stepOrdinal, int craftTypeSlot)</c>.
    /// </summary>
    private static readonly Dictionary<string, int> StepOrdinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skysteel +1"] = 1,
        ["Dragonsung"] = 2,
        ["Augmented Dragonsung"] = 3,
        ["Skysung"] = 4,
        ["Skybuilders'"] = 5,
        ["Augmented"] = 6,
        ["Crystalline"] = 7,
        ["Chora-Zoi's"] = 8,
        ["Brilliant"] = 9,
        ["Vrandtic"] = 10,
        ["Lodestar"] = 11,
    };

    private static ICallGateSubscriber<int, int, int>? _getRelicToolListId;
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

    public static bool SupportsRelicToolLists
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
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
    }

    public static bool TryGetRelicToolListId(string stepName, int craftSlot, out int listId)
    {
        listId = 0;
        if (!StepOrdinals.TryGetValue(stepName, out int stepOrdinal))
        {
            return false;
        }

        if (!SupportsRelicToolLists || _getRelicToolListId == null)
        {
            return false;
        }

        try
        {
            listId = _getRelicToolListId.InvokeFunc(stepOrdinal, craftSlot);
            return listId != 0;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan.GetRelicToolListId failed for {Step} ({Ordinal}) slot {Slot}", stepName, stepOrdinal, craftSlot);
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
        if (!SupportsRelicToolLists || _isBusy == null)
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
            _getRelicToolListId ??= Svc.PluginInterface.GetIpcSubscriber<int, int, int>("Artisan.GetRelicToolListId");
            _startListById ??= Svc.PluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");
            _isBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");

            if (!_getRelicToolListId.HasFunction)
            {
                return;
            }

            _ = _getRelicToolListId.InvokeFunc(0, 0);
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
