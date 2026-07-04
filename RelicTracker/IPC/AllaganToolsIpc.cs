using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
namespace RelicTracker.IPC;

internal static class AllaganToolsIpc
{
    private static readonly string[] PluginNames = ["Allagan Tools", "InventoryTools", "AllaganTools"];

    private static readonly uint[] AllInventoryTypes = [.. Enum.GetValues<InventoryType>().Select(static t => (uint)t)];

    private static ICallGateSubscriber<bool, bool>? _initialized;
    private static ICallGateSubscriber<bool>? _isInitialized;
    private static ICallGateSubscriber<uint, bool, uint[], uint>? _itemCountOwned;
    private static bool _ipcBound;

    public static bool IsInstalled =>
        PluginNames.Any(name => Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.Name == name));

    public static bool IsEnabled =>
        PluginNames.Any(name => DalamudReflector.TryGetDalamudPlugin(name, out var _, false, true));

    public static bool IsReady =>
        IsEnabled && _ipcBound && _itemCountOwned != null && InvokeIsInitialized();

    public static void Init()
    {
        _initialized = Svc.PluginInterface.GetIpcSubscriber<bool, bool>("AllaganTools.Initialized");
        _isInitialized = Svc.PluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        _initialized.Subscribe(OnAllaganToolsInitialized);
        OnAllaganToolsInitialized(true);
    }

    public static void Dispose()
    {
        _initialized?.Unsubscribe(OnAllaganToolsInitialized);
        _initialized = null;
        _isInitialized = null;
        _itemCountOwned = null;
        _ipcBound = false;
    }

    public static uint GetOwnedCount(uint itemId, bool activeCharacterOnly)
    {
        if (!IsReady || _itemCountOwned == null)
        {
            return 0;
        }

        try
        {
            return _itemCountOwned.InvokeFunc(itemId, activeCharacterOnly, AllInventoryTypes);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Allagan Tools IPC query failed for item {ItemId}", itemId);
            return 0;
        }
    }

    private static void OnAllaganToolsInitialized(bool _)
    {
        if (_ipcBound || !InvokeIsInitialized())
        {
            return;
        }

        try
        {
            _itemCountOwned = Svc.PluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
            _ipcBound = true;
            Svc.Log.Information("[RelicTracker] Allagan Tools IPC ready.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] Failed to bind Allagan Tools IPC.");
        }
    }

    private static bool InvokeIsInitialized()
    {
        if (_isInitialized == null)
        {
            return false;
        }

        try
        {
            return _isInitialized.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }
}
