using FFXIVClientStructs.FFXIV.Client.Game;

namespace RelicTracker.Framework;

/// <summary>
///     Live on-character counts via <see cref="InventoryManager"/>, including Occult currency
///     containers Allagan Tools may not index. Does not include retainers, armoire, or dresser.
/// </summary>
internal static unsafe class PlayerInventory
{
    public static uint GetItemCount(uint itemId)
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
