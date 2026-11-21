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
    private int cacheGeneration;
    private bool inventoryCountsDirty;
    private long lastInventoryCountsInvalidateTick;

    // Fires constantly in Eureka and other loot-heavy zones. Don't wipe relic ownership here: that rebuild walks
    // every relic via MogVault and hitched frames. Ownership still rolls on the 10s stamp.
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
        collectInputInitialized = false;
        InvalidateOwnershipCache();
    }

    public void OnCharacterLoggedOut(int type, int code)
    {
        config.PersistIfDirty();
        collectInputInitialized = false;
        InvalidateOwnershipCache();
    }

    public void OpenTo(RelicItemTarget target)
    {
        inspectedTier = null;
        armorSelection = null;
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
        else if (target.Tab == RelicTrackerDestinationTab.Relic && !string.IsNullOrEmpty(target.CollectType))
        {
            var currentJob = Svc.PlayerState.ClassJob.ValueNullable?.Abbreviation.ToString();
            var relic = catalog.LinesFor(target.ExpansionId).FirstOrDefault(line => line.CollectType == target.CollectType);
            if (currentJob is not null && relic?.EffectiveJobList.Contains(currentJob, StringComparer.Ordinal) == true)
            {
                config.DetailJob = currentJob;
            }
        }

        pendingArmorFocus = target.ArmorSet is not null && target.ArmorPiece >= 0
            ? (target.ArmorSet, target.ArmorTier, target.ArmorPiece)
            : null;
        if (target.Tab == RelicTrackerDestinationTab.Tracker)
        {
            materialFilter = target.Search ?? string.Empty;
        }

        config.TrackerLineFilter = target.Tab == RelicTrackerDestinationTab.Relic
            ? target.CollectType ?? string.Empty
            : string.Empty;

        page = target.Tab switch
        {
            RelicTrackerDestinationTab.Relic => Page.Upgrade,
            RelicTrackerDestinationTab.Tracker => Page.Materials,
            _ => Page.Collection,
        };
        config.OnSettingChanged();
        IsOpen = true;
    }

    private IReadOnlyList<RelicLineStatus>? cachedStatuses;
    private RelicOwnership? cachedStatusesOwnership;
    private bool cachedStatusesHidePhyseos;

    private IReadOnlyList<RelicLineStatus> GetStatuses()
    {
        var ownership = GetOwnership();
        if (cachedStatuses is null
            || !ReferenceEquals(ownership, cachedStatusesOwnership)
            || cachedStatusesHidePhyseos != config.HidePhyseosRelics)
        {
            cachedStatuses = RelicStatusService.Build(ownership, catalog, config.HidePhyseosRelics);
            cachedStatusesOwnership = ownership;
            cachedStatusesHidePhyseos = config.HidePhyseosRelics;
        }

        return cachedStatuses;
    }

    private RelicOwnership GetOwnership()
    {
        ulong collectCharacterId = config.CollectCharacterId;
        ulong localContentId = CharacterScope.CurrentContentId;
        DateTime? stamp = ffxivCollect.LastRefreshUtc;
        long inventoryStamp = InventoryCacheStamp();
        if (cachedOwnership is null
            || cachedOwnershipStamp != stamp
            || cachedOwnershipCharacterId != collectCharacterId
            || cachedLocalContentId != localContentId
            || cachedOwnershipInventoryStamp != inventoryStamp)
        {
            FfxivCollectSnapshot snapshot = collectCharacterId != 0 && ffxivCollect.Snapshot.CharacterId == collectCharacterId
                ? ffxivCollect.Snapshot
                : FfxivCollectSnapshot.Empty;
            CharacterProgress progress = config.CurrentCharacterProgress();
            HashSet<string> inventoryDone;
            HashSet<string> inventoryArmorDone;
            if (MogVaultIpc.IsReady)
            {
                Func<uint, uint> ownedLookup = CreateOwnedLookup();
                inventoryDone = RelicStatusService.BuildStepDoneKeys(catalog, ownedLookup);
                inventoryArmorDone = RelicStatusService.BuildArmorPieceDoneKeys(catalog, ownedLookup);
                if (!WksCosmicTools.IsResearchLoaded)
                {
                    // Research stages only go up; keep the credit saved while Cosmic Exploration data isn't loaded.
                    inventoryDone.UnionWith(progress.InventoryStepDone.Where(key =>
                        key.StartsWith(WksCosmicTools.CollectType + "|", StringComparison.Ordinal)));
                }

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
        MogVaultIpc.IsBound ? Environment.TickCount64 / InventoryCacheBucketMs : 0;

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
        bool mogVaultReady = MogVaultIpc.IsReady;
        return itemId =>
        {
            if (!cache.TryGetValue(itemId, out uint count))
            {
                count = GetOnCharacterItemCount(itemId);
                uint mogVault = mogVaultReady ? MogVaultIpc.GetOwnedCount(itemId) : 0;
                if (mogVault > count)
                {
                    count = mogVault;
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
