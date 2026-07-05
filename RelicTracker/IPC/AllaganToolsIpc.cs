using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
namespace RelicTracker.IPC;

internal static class AllaganToolsIpc
{
    private static readonly string[] PluginNames = ["Allagan Tools", "InventoryTools", "AllaganTools"];

    /// <summary>
    ///     Allagan Tools stores armoire and glamour dresser items under CriticalCommonLib container IDs
    ///     that are not part of FFXIVClientStructs' <see cref="InventoryType"/> enum.
    /// </summary>
    private const uint ArmoireContainer = 2500;

    private const uint GlamourChestContainer = 2501;

    private static readonly uint[] TrackedInventoryTypes = BuildTrackedInventoryTypes();

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
            return _itemCountOwned.InvokeFunc(itemId, activeCharacterOnly, TrackedInventoryTypes);
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

    private static uint[] BuildTrackedInventoryTypes()
    {
        HashSet<uint> types = new(Enum.GetValues<InventoryType>().Length + 2);
        foreach (InventoryType inventoryType in Enum.GetValues<InventoryType>())
        {
            types.Add((uint)inventoryType);
        }

        types.Add(ArmoireContainer);
        types.Add(GlamourChestContainer);
        return [.. types];
    }
}
