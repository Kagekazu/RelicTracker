using Dalamud.Game.Inventory.InventoryEventArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private const long InventoryCacheBucketMs = 10_000;
    private const long TrackerInventoryRefreshMs = 2_000;
    private const long InventoryCountsDebounceMs = 1_000;

    private RelicOwnership? cachedOwnership;
    private ulong cachedLocalContentId;
    private ulong cachedOwnershipCharacterId;
    private long cachedOwnershipInventoryStamp;
    private DateTime? cachedOwnershipStamp;
    private Dictionary<uint, uint>? ownedCountCache;
    private long ownedCountCacheStamp;
    private RelicTrackerDestinationTab? pendingTab;
    private int cacheGeneration;
    private bool inventoryCountsDirty;
    private long lastInventoryCountsInvalidateTick;

    /// <summary>
    ///     Eureka (and other loot-heavy zones) fire this constantly. Do not wipe relic ownership here —
    ///     that rebuild walks every relic via Allagan Tools and used to hitch frames. Material counts are
    ///     refreshed on the next Draw with a short debounce; ownership still rolls on the 10s stamp.
    /// </summary>
    public void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> _) =>
        inventoryCountsDirty = true;

    private void FlushInventoryCountInvalidation()
    {
        if (!inventoryCountsDirty)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (lastInventoryCountsInvalidateTick != 0
            && now - lastInventoryCountsInvalidateTick < InventoryCountsDebounceMs)
        {
            return;
        }

        inventoryCountsDirty = false;
        lastInventoryCountsInvalidateTick = now;
        InvalidateOwnedCountCache();
        InvalidateShoppingCache();
    }

    public void OnCharacterChanged()
    {
        config.MigrateLegacyProgressIfNeeded();
        InvalidateOwnershipCache();
    }

    public void OnCharacterLoggedOut(int type, int code) => InvalidateOwnershipCache();

    public void OpenTo(RelicItemTarget target)
    {
        config.SelectedExpansionId = target.ExpansionId;
        config.DetailExpansionId = target.ExpansionId;
        if (!string.IsNullOrEmpty(target.CollectType))
        {
            config.DetailCollectType = target.CollectType;
        }

        if (!string.IsNullOrEmpty(target.Job))
        {
            config.DetailJob = target.Job;
        }

        if (target.Tab == RelicTrackerDestinationTab.Tracker)
        {
            config.TrackerLineFilter = string.Empty;
        }

        pendingTab = target.Tab;
        config.OnSettingChanged();
        IsOpen = true;
    }

    private ImGuiTabItemFlags TabOpenFlags(RelicTrackerDestinationTab tab) =>
        pendingTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

    private void ConsumePendingTab(RelicTrackerDestinationTab tab)
    {
        if (pendingTab == tab)
        {
            pendingTab = null;
        }
    }

    private RelicOwnership GetOwnership()
    {
        ulong collectCharacterId = config.FfxivCollectCharacterId;
        ulong localContentId = CharacterScope.CurrentContentId;
        DateTime? stamp = ffxivCollect.LastRefreshUtc;
        long inventoryStamp = InventoryCacheStamp();
        if (cachedOwnership is null
            || cachedOwnershipStamp != stamp
            || cachedOwnershipCharacterId != collectCharacterId
            || cachedLocalContentId != localContentId
            || cachedOwnershipInventoryStamp != inventoryStamp)
        {
            FfxivCollectSnapshot snapshot = collectCharacterId == 0 ? FfxivCollectSnapshot.Empty : ffxivCollect.Snapshot;
            CharacterProgress progress = config.CurrentCharacterProgress();
            HashSet<string> inventoryDone;
            HashSet<string> inventoryArmorDone;
            if (AllaganToolsIpc.IsReady)
            {
                Func<uint, uint> ownedLookup = CreateOwnedLookup();
                inventoryDone = RelicStatusService.BuildStepDoneKeys(catalog, ownedLookup);
                inventoryArmorDone = RelicStatusService.BuildArmorPieceDoneKeys(catalog, ownedLookup);
                config.SaveInventorySnapshot(inventoryDone, inventoryArmorDone);
            }
            else
            {
                inventoryDone = new HashSet<string>(progress.InventoryStepDone, StringComparer.Ordinal);
                inventoryArmorDone = new HashSet<string>(progress.InventoryArmorPieceDone, StringComparer.Ordinal);
            }

            cachedOwnership = new(
                snapshot,
                progress.RelicStepDone,
                progress.ArmorPieceDone,
                inventoryDone,
                inventoryArmorDone);
            cachedOwnershipStamp = stamp;
            cachedOwnershipCharacterId = collectCharacterId;
            cachedLocalContentId = localContentId;
            cachedOwnershipInventoryStamp = inventoryStamp;
        }

        return cachedOwnership;
    }

    private void InvalidateOwnershipCache()
    {
        cachedOwnership = null;
        cachedOwnershipStamp = null;
        cachedOwnershipCharacterId = 0;
        cachedLocalContentId = 0;
        cachedOwnershipInventoryStamp = 0;
        cacheGeneration++;
        InvalidateShoppingCache();
        InvalidateOwnedCountCache();
    }

    private static long InventoryCacheStamp() =>
        AllaganToolsIpc.IsBound ? Environment.TickCount64 / InventoryCacheBucketMs : 0;

    private long OwnedCountRefreshStamp()
    {
        long interval = trackerTabVisible ? TrackerInventoryRefreshMs : InventoryCacheBucketMs;
        return Environment.TickCount64 / interval;
    }

    private Func<uint, uint> CreateOwnedLookup()
    {
        long stamp = OwnedCountRefreshStamp();
        if (ownedCountCache is null || ownedCountCacheStamp != stamp)
        {
            ownedCountCache = new Dictionary<uint, uint>();
            ownedCountCacheStamp = stamp;
        }

        Dictionary<uint, uint> cache = ownedCountCache;
        return itemId =>
        {
            if (!cache.TryGetValue(itemId, out uint count))
            {
                count = GetOnCharacterItemCount(itemId);
                uint allagan = AllaganToolsIpc.GetOwnedCount(itemId, activeCharacterOnly: true);
                if (allagan > count)
                {
                    count = allagan;
                }

                cache[itemId] = count;
            }

            return count;
        };
    }

    private void InvalidateOwnedCountCache()
    {
        ownedCountCache = null;
        ownedCountCacheStamp = 0;
    }

    private static unsafe uint GetOnCharacterItemCount(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        InventoryManager* inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return 0;
        }

        int nq = inventory->GetInventoryItemCount(itemId);
        int hq = inventory->GetInventoryItemCount(itemId, isHq: true);
        return (uint)Math.Max(0, nq) + (uint)Math.Max(0, hq);
    }
}
