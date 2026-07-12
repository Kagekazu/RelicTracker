using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Game.Text.SeStringHandling;

namespace RelicTracker;

internal sealed class RelicContextMenu : IDisposable
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

        GameInventoryItem? item = inventory.TargetItem;
        if (item is null || item.Value.IsEmpty)
        {
            return;
        }

        uint itemId = item.Value.BaseItemId;
        if (!index.TryGet(itemId, out RelicItemTarget? target))
        {
            return;
        }

        RelicItemTarget navigation = target;
        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder().Append(navigation.MenuLabel).BuiltString,
            OnClicked = _ => ui.OpenTo(navigation),
        });
    }
}
