using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

/// <summary>
///     Client for Gather &amp; Chill's IPC: adds missing gatherables to its gather list (v1), and
///     gathers one item right away or stops (v2).
///     Provider: GatherChill/IPC/GatherChillProvider.cs in the Gather &amp; Chill repo.
/// </summary>
internal static class GatherChillIpc
{
    private const string InternalName = "GatherChill";
    private const int RequiredApiVersion = 1;
    private const int GatherItemApiVersion = 2;
    private const long BindRetryMs = 60_000;

    private static ICallGateSubscriber<int>? _apiVersion;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static ICallGateSubscriber<uint, bool>? _canGather;
    private static ICallGateSubscriber<(uint ItemId, uint Amount)[], int>? _addToGatherList;
    private static ICallGateSubscriber<uint, int, bool>? _gatherItem;
    private static ICallGateSubscriber<object>? _stop;
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

    /// <summary>True if this Gather &amp; Chill can gather one item directly (API v2+).</summary>
    public static bool SupportsGatherItem => IsReady && ApiVersion() >= GatherItemApiVersion;

    public static void Init() => EnsureBound();

    public static void Dispose()
    {
        _apiVersion = null;
        _isBusy = null;
        _canGather = null;
        _addToGatherList = null;
        _gatherItem = null;
        _stop = null;
        CanGatherCache.Clear();
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
    }

    /// <summary>Queried each time, so a Gather &amp; Chill update is picked up without a reload.</summary>
    public static int ApiVersion()
    {
        if (!IsReady || _apiVersion == null)
        {
            return 0;
        }

        try
        {
            return _apiVersion.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] GatherChill.ApiVersion failed.");
            return 0;
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

    /// <summary>
    ///     Adds the item to Gather &amp; Chill's gather list (an existing entry keeps the larger amount)
    ///     and starts gathering only that item. Gather &amp; Chill stops by itself when it's done.
    /// </summary>
    public static bool TryGatherItem(uint itemId, uint amount, out string? error)
    {
        error = null;
        if (!SupportsGatherItem || _gatherItem == null)
        {
            error = "Gather & Chill isn't loaded, or is too old.";
            return false;
        }

        try
        {
            if (_gatherItem.InvokeFunc(itemId, (int)amount))
            {
                return true;
            }

            error = IsBusy() ? "Gather & Chill is already gathering." : "Gather & Chill couldn't gather that item.";
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] GatherChill.GatherItem failed for {ItemId}.", itemId);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Stops Gather &amp; Chill, same as its Stop button (API v2+).</summary>
    public static bool TryStop(out string? error)
    {
        error = null;
        if (!SupportsGatherItem || _stop == null)
        {
            error = "Gather & Chill isn't loaded, or is too old.";
            return false;
        }

        try
        {
            _stop.InvokeAction();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] GatherChill.Stop failed.");
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
            _gatherItem ??= Svc.PluginInterface.GetIpcSubscriber<uint, int, bool>("GatherChill.GatherItem");
            _stop ??= Svc.PluginInterface.GetIpcSubscriber<object>("GatherChill.Stop");

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
