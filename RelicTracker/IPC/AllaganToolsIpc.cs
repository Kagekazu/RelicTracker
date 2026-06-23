using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RelicTracker.IPC;

internal static class AllaganToolsIpc
{
    private static readonly uint[] AllInventoryTypes = Enum.GetValues<InventoryType>()
        .Select(static t => (uint)t)
        .ToArray();

    private static ICallGateSubscriber<bool>? _isInitialized;
    private static ICallGateSubscriber<uint, ulong, int, uint>? _itemCount;
    private static ICallGateSubscriber<uint, bool, uint[], uint>? _itemCountOwned;
    private static ICallGateSubscriber<bool, HashSet<ulong>>? _getCharactersOwnedByActive;

    public static bool IsPluginLoaded =>
        Svc.PluginInterface.InstalledPlugins.Any(p =>
            p.InternalName == RelicTrackerConstants.AllaganToolsPluginName && p.IsLoaded);

    public static bool IsReady => IsPluginLoaded && (_isInitialized?.InvokeFunc() ?? false);

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _isInitialized = pluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        _itemCount = pluginInterface.GetIpcSubscriber<uint, ulong, int, uint>("AllaganTools.ItemCount");
        _itemCountOwned = pluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
        _getCharactersOwnedByActive = pluginInterface.GetIpcSubscriber<bool, HashSet<ulong>>("AllaganTools.GetCharactersOwnedByActive");
    }

    public static uint GetOwnedCount(uint itemId, bool activeCharacterOnly)
    {
        if (!IsReady)
        {
            return 0;
        }

        try
        {
            if (!activeCharacterOnly && _itemCountOwned != null)
            {
                return _itemCountOwned.InvokeFunc(itemId, false, AllInventoryTypes);
            }

            if (_itemCount == null || _getCharactersOwnedByActive == null)
            {
                return 0;
            }

            var characterIds = _getCharactersOwnedByActive.InvokeFunc(true);
            uint total = 0;
            foreach (var characterId in characterIds)
            {
                total += _itemCount.InvokeFunc(itemId, characterId, -1);
            }

            return total;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Allagan Tools IPC query failed for item {ItemId}", itemId);
            return 0;
        }
    }
}
