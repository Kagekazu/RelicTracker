using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace RelicTracker;

internal sealed unsafe class RelicContextMenu : IDisposable
{
    private readonly RelicItemNavigationIndex index;
    private readonly PluginUI ui;

    public RelicContextMenu(PluginUI ui, RelicItemNavigationIndex index)
    {
        this.ui = ui;
        this.index = index;
        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory)
        {
            return;
        }

        if (args.Target is not MenuTargetInventory inventory)
        {
            return;
        }

        if (!TryResolveInventoryItem(args, inventory, out GameInventoryItem item))
        {
            return;
        }

        if (!index.TryGet(item, out RelicItemTarget navigation))
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder().Append(navigation.MenuLabel).BuiltString,
            PrefixChar = 'R',
            PrefixColor = IMenuItem.DalamudDefaultPrefixColor,
            OnClicked = _ => ui.OpenTo(navigation),
        });
    }

    private static bool TryResolveInventoryItem(
        IMenuOpenedArgs args,
        MenuTargetInventory inventory,
        out GameInventoryItem item)
    {
        if (inventory.TargetItem is { IsEmpty: false } target)
        {
            item = target;
            return true;
        }

        AgentInventoryContext* agent = (AgentInventoryContext*)args.AgentPtr;
        return TryGetSlotItem(agent->TargetInventoryId, agent->TargetInventorySlotId, out item);
    }

    private static bool TryGetSlotItem(InventoryType inventoryType, int slot, out GameInventoryItem item)
    {
        item = default;
        if (slot < 0)
        {
            return false;
        }

        ReadOnlySpan<GameInventoryItem> items =
            Svc.GameInventory.GetInventoryItems((GameInventoryType)(uint)inventoryType);
        if (slot >= items.Length)
        {
            return false;
        }

        item = items[slot];
        return !item.IsEmpty;
    }
}
