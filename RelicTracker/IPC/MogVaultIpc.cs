using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

internal static class MogVaultIpc
{
    private const string InternalName = "MogVault";
    private static readonly Version MinimumPluginVersion = new(1, 0, 0, 0);
    private const int RequiredContractVersion = 1;

    private static ICallGateSubscriber<int>? _version;
    private static ICallGateSubscriber<bool>? _isReady;
    private static ICallGateSubscriber<object>? _ready;
    private static ICallGateSubscriber<uint, bool, long>? _getItemCount;
    private static bool _ipcBound;

    public static bool IsInstalled =>
        Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == InternalName);

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin(InternalName, out _, false, true);

    public static bool IsBound => _ipcBound && _getItemCount != null;

    public static bool IsReady => IsBound && IsEnabled && HasRequiredContract() && InvokeIsReady();

    public static void Init()
    {
        _version = Svc.PluginInterface.GetIpcSubscriber<int>("MogVault.Version");
        _isReady = Svc.PluginInterface.GetIpcSubscriber<bool>("MogVault.IsReady");
        _ready = Svc.PluginInterface.GetIpcSubscriber<object>("MogVault.Ready");
        _ready.Subscribe(OnMogVaultReady);
        OnMogVaultReady();
    }

    public static void Dispose()
    {
        _ready?.Unsubscribe(OnMogVaultReady);
        _version = null;
        _isReady = null;
        _ready = null;
        _getItemCount = null;
        _ipcBound = false;
    }

    public static uint GetOwnedCount(uint itemId, bool activeCharacterOnly)
    {
        // RelicTracker only tracks the active character. MogVault's count is already scoped to it.
        if (!IsReady || _getItemCount == null)
        {
            return 0;
        }

        try
        {
            long count = _getItemCount.InvokeFunc(itemId, true);
            return count <= 0 ? 0 : (uint)Math.Min(count, uint.MaxValue);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] MogVault IPC query failed for item {ItemId}.", itemId);
            return 0;
        }
    }

    private static void OnMogVaultReady()
    {
        if (_ipcBound || !HasRequiredContract() || !InvokeIsReady())
        {
            return;
        }

        try
        {
            _getItemCount = Svc.PluginInterface.GetIpcSubscriber<uint, bool, long>("MogVault.GetItemCount");
            _ipcBound = true;
            Svc.Log.Information("[RelicTracker] MogVault IPC ready.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] Failed to bind MogVault IPC.");
        }
    }

    private static bool HasRequiredContract()
    {
        if (_version is not { HasFunction: true } || !Svc.PluginInterface.InstalledPlugins.Any(
            plugin => plugin.InternalName == InternalName && plugin.Version >= MinimumPluginVersion))
        {
            return false;
        }

        try
        {
            return _version.InvokeFunc() == RequiredContractVersion;
        }
        catch
        {
            return false;
        }
    }

    private static bool InvokeIsReady()
    {
        if (_isReady == null)
        {
            return false;
        }

        try
        {
            return _isReady.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }
}
