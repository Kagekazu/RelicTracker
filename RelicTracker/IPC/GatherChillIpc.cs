using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

internal static class GatherChillIpc
{
    private const string InternalName = "GatherChill";
    private static readonly Version MinimumPluginVersion = new(1, 0, 0, 6);
    private const int RequiredApiVersion = 5;
    private const int JobStarted = 0;
    private const int JobRunning = 1;
    private const int JobStopping = 2;
    private const string JobOwner = "RelicTracker";
    private const long BindRetryMs = 60_000;

    private static ICallGateSubscriber<int>? _apiVersion;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static ICallGateSubscriber<uint, bool>? _canGather;
    private static ICallGateSubscriber<(uint ItemId, uint Amount)[], int>? _addToGatherList;
    private static ICallGateSubscriber<string, uint, int, (int Result, Guid JobId, string Message)>? _startJob;
    private static ICallGateSubscriber<Guid, (int State, int Gathered, int Wanted, string Message)>? _getJob;
    private static ICallGateSubscriber<string, Guid, bool>? _cancelJob;
    private static Guid _jobId;
    private static bool _busy;
    private static long _busyCheckedTick;
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
            return _ipcBound && ApiVersion() >= RequiredApiVersion;
        }
    }

    public static void Init() => EnsureBound();

    public static void Dispose()
    {
        _apiVersion = null;
        _isBusy = null;
        _canGather = null;
        _addToGatherList = null;
        _startJob = null;
        _getJob = null;
        _cancelJob = null;
        _jobId = Guid.Empty;
        CanGatherCache.Clear();
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
    }

    // Queried each time, so a Gather & Chill update is picked up without a reload.
    private static int ApiVersion()
    {
        if (_apiVersion is not { HasFunction: true })
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

    // Every material row asks each frame; one answer per 250 ms is plenty.
    public static bool IsBusy()
    {
        long now = Environment.TickCount64;
        if (now - _busyCheckedTick < 250)
        {
            return _busy;
        }

        _busyCheckedTick = now;
        if (!IsReady || _isBusy == null)
        {
            return _busy = false;
        }

        try
        {
            return _busy = _isBusy.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] GatherChill.IsBusy failed.");
            return _busy = false;
        }
    }

    // Cached; routes rarely change.
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

    // Amounts are how many to gather; an item already listed keeps the larger amount.
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
            error = "Gather & Chill didn't accept the list. Try again.";
            return false;
        }
    }

    public static bool TryStartJob(uint itemId, uint amount, out string? error)
    {
        error = null;
        if (!IsReady || _startJob == null)
        {
            error = "Gather & Chill's temporary jobs aren't available.";
            return false;
        }

        try
        {
            var result = _startJob.InvokeFunc(JobOwner, itemId, (int)amount);
            if (result.Result == JobStarted && result.JobId != Guid.Empty)
            {
                _jobId = result.JobId;
                return true;
            }

            error = result.Message;
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] GatherChill.StartJob failed for {ItemId}.", itemId);
            error = "Gather & Chill didn't start gathering. Try again.";
            return false;
        }
    }

    public static bool CanCancelJob()
    {
        if (_jobId == Guid.Empty || !IsReady || _getJob == null)
        {
            return false;
        }

        try
        {
            var state = _getJob.InvokeFunc(_jobId).State;
            if (state is not (JobRunning or JobStopping))
            {
                _jobId = Guid.Empty;
            }

            return state == JobRunning;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] GatherChill.GetJob failed.");
            return false;
        }
    }

    public static bool TryCancelJob(out string? error)
    {
        error = null;
        if (!IsReady || _jobId == Guid.Empty || _cancelJob == null)
        {
            error = "No RelicTracker gathering job is available to stop.";
            return false;
        }

        try
        {
            if (_cancelJob.InvokeFunc(JobOwner, _jobId))
            {
                return true;
            }

            error = "Gather & Chill couldn't stop the RelicTracker job.";
            return false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] GatherChill.CancelJob failed.");
            error = "Gather & Chill didn't stop. Try again.";
            return false;
        }
    }

    private static void EnsureBound()
    {
        if (_ipcBound)
        {
            if (_apiVersion?.HasFunction == true)
            {
                return;
            }

            // Gather & Chill was disabled or unloaded. Drop the subscribers too: a reload can bring new gates.
            Dispose();
            Svc.Log.Information("[RelicTracker] Gather & Chill IPC gone.");
        }

        if (!IsEnabled || !Svc.PluginInterface.InstalledPlugins.Any(
            plugin => plugin.InternalName == InternalName && plugin.Version >= MinimumPluginVersion))
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
            _startJob ??= Svc.PluginInterface
                .GetIpcSubscriber<string, uint, int, (int, Guid, string)>("GatherChill.StartJob");
            _getJob ??= Svc.PluginInterface
                .GetIpcSubscriber<Guid, (int, int, int, string)>("GatherChill.GetJob");
            _cancelJob ??= Svc.PluginInterface.GetIpcSubscriber<string, Guid, bool>("GatherChill.CancelJob");

            if (ApiVersion() < RequiredApiVersion || !_startJob.HasFunction || !_getJob.HasFunction || !_cancelJob.HasFunction)
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
