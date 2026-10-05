using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

/// <summary>
///     Client for Gather &amp; Chill's IPC (API v1): adds missing gatherables to its gather list.
///     Provider: GatherChill/IPC/GatherChillProvider.cs in the Gather &amp; Chill repo.
/// </summary>
internal static class GatherChillIpc
{
    private const string InternalName = "GatherChill";
    private const int RequiredApiVersion = 1;
    private const long BindRetryMs = 60_000;

    private static ICallGateSubscriber<int>? _apiVersion;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static ICallGateSubscriber<uint, bool>? _canGather;
    private static ICallGateSubscriber<(uint ItemId, uint Amount)[], int>? _addToGatherList;
    private static readonly Dictionary<uint, bool> CanGatherCache = [];
    private static bool _ipcBound;
    private static bool _loggedBindFailure;
    private static long _lastBindAttemptTick;

    public static bool IsInstalled =>
        DalamudReflector.TryGetDalamudPlugin(InternalName, out _, false, true)
        || Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == InternalName);

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin(InternalName, out _, false, true);

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
        _apiVersion = null;
        _isBusy = null;
        _canGather = null;
        _addToGatherList = null;
        CanGatherCache.Clear();
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
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
            Svc.Log.Debug(ex, "[RelicTracker] GatherChill.IsBusy failed.");
            return false;
        }
    }

    /// <summary>True if Gather &amp; Chill has a route for the item. Cached; routes rarely change.</summary>
    public static bool CanGather(uint itemId)
    {
        if (!IsReady || _canGather == null)
        {
            return false;
        }

        if (CanGatherCache.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        try
        {
            var result = _canGather.InvokeFunc(itemId);
            CanGatherCache[itemId] = result;
            return result;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] GatherChill.CanGather failed for {ItemId}.", itemId);
            return false;
        }
    }

    /// <summary>
    ///     Adds items to Gather &amp; Chill's gather list; amounts are how many to gather, and an
    ///     item already listed keeps the larger amount. Returns how many were added or updated.
    /// </summary>
    public static bool TryAddToGatherList((uint ItemId, uint Amount)[] items, out int added, out string? error)
    {
        added = 0;
        error = null;
        if (!IsReady || _addToGatherList == null)
        {
            error = "Gather & Chill isn't loaded, or is too old.";
            return false;
        }

        try
        {
            added = _addToGatherList.InvokeFunc(items);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] GatherChill.AddToGatherList failed.");
            error = ex.Message;
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
            _apiVersion ??= Svc.PluginInterface.GetIpcSubscriber<int>("GatherChill.ApiVersion");
            _isBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("GatherChill.IsBusy");
            _canGather ??= Svc.PluginInterface.GetIpcSubscriber<uint, bool>("GatherChill.CanGather");
            _addToGatherList ??= Svc.PluginInterface.GetIpcSubscriber<(uint ItemId, uint Amount)[], int>(
                "GatherChill.AddToGatherList");

            if (!_apiVersion.HasFunction || _apiVersion.InvokeFunc() < RequiredApiVersion)
            {
                return;
            }

            _ipcBound = true;
            _loggedBindFailure = false;
            Svc.Log.Information("[RelicTracker] Gather & Chill IPC ready.");
        }
        catch (Exception ex)
        {
            if (!_loggedBindFailure)
            {
                Svc.Log.Debug(ex, "[RelicTracker] Gather & Chill IPC not available yet.");
                _loggedBindFailure = true;
            }
        }
    }
}
