using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace RelicTracker;

public enum RelicTrackerDestinationTab
{
    Overview,
    Relic,
    Tracker,
}

public sealed record RelicItemTarget(
    RelicTrackerDestinationTab Tab,
    string ExpansionId,
    string MenuLabel,
    string? CollectType = null,
    string? Job = null);

public sealed class RelicItemNavigationIndex
{
    private readonly Dictionary<uint, RelicItemTarget> byItemId = [];

    public RelicItemNavigationIndex(RelicDataService data, RelicCatalog catalog)
    {
        IndexMaterials(data);
        IndexArmorCurrency(data);
        IndexArmorPieces(catalog);
        IndexRelics(catalog);
        Svc.Log.Information("[RelicTracker] Indexed {Count} relic-related item IDs for context menu.", byItemId.Count);
    }

    public bool TryGet(GameInventoryItem item, out RelicItemTarget target)
    {
        target = null!;
        if (item.IsEmpty)
        {
            return false;
        }

        if (TryGet(item.BaseItemId, out target))
        {
            return true;
        }

        return item.ItemId != item.BaseItemId && TryGet(item.ItemId, out target);
    }

    private bool TryGet(uint itemId, out RelicItemTarget target) =>
        byItemId.TryGetValue(itemId, out target!);

    private void IndexMaterials(RelicDataService data)
    {
        foreach ((string expansionId, ExpansionSheet sheet) in data.Expansions)
        {
            HashSet<uint> seen = [];
            foreach (ExpansionMaterialRow row in sheet.Materials)
            {
                if (row.MaterialIds.Count == 0)
                {
                    continue;
                }

                var material = row.Material?.Trim();
                var label = !string.IsNullOrWhiteSpace(material)
                            && data.MaterialSources.TryGetValue(material, out string? source)
                    ? $"Open Tracker — {source}"
                    : $"Open Tracker — {ExpansionNames.LongName(expansionId)}";

                var target = new RelicItemTarget(RelicTrackerDestinationTab.Tracker, expansionId, label);
                foreach (uint itemId in row.MaterialIds)
                {
                    if (itemId > 0 && seen.Add(itemId))
                    {
                        Add(itemId, target);
                    }
                }
            }
        }
    }

    private void IndexArmorCurrency(RelicDataService data)
    {
        foreach ((string expansionId, List<ArmorCostRow> costs) in data.ArmorCosts)
        {
            foreach (ArmorCostRow cost in costs)
            {
                if (cost.CurrencyIds.Count == 0)
                {
                    continue;
                }

                var label = data.MaterialSources.TryGetValue(cost.Currency, out string? source)
                    ? $"Open Tracker — {source}"
                    : $"Open Tracker — {ExpansionNames.LongName(expansionId)}";
                var target = new RelicItemTarget(RelicTrackerDestinationTab.Tracker, expansionId, label);
                foreach (uint itemId in cost.CurrencyIds)
                {
                    Add(itemId, target);
                }
            }
        }
    }

    private void IndexArmorPieces(RelicCatalog catalog)
    {
        foreach (ArmorLine line in catalog.ArmorLines)
        {
            var label = $"Open Relic — {line.LineName}";
            var target = new RelicItemTarget(
                RelicTrackerDestinationTab.Relic,
                line.Expansion,
                label,
                CollectType: line.LineName);
            foreach (ArmorTier tier in line.AllTiers)
            {
                foreach (uint itemId in tier.PieceIds)
                {
                    Add(itemId, target, preferRelic: true);
                }
            }
        }
    }

    private void IndexRelics(RelicCatalog catalog)
    {
        foreach (RelicLine line in catalog.Lines)
        {
            IReadOnlyList<string> jobs = line.EffectiveJobList;
            for (int slot = 0; slot < line.Jobs; slot++)
            {
                string? job = slot < jobs.Count ? jobs[slot] : null;
                for (int tier = 0; tier < line.TierCount; tier++)
                {
                    uint relicId = line.RelicId(slot, tier);
                    if (relicId > 0)
                    {
                        AddRelic(relicId, line, job);
                    }

                    foreach (uint replicaId in line.RelicReplicas(slot, tier))
                    {
                        AddRelic(replicaId, line, job);
                    }
                }
            }
        }
    }

    private void AddRelic(uint itemId, RelicLine line, string? job)
    {
        Add(
            itemId,
            new RelicItemTarget(
                RelicTrackerDestinationTab.Relic,
                line.Expansion,
                $"Open Relic — {line.CollectType}",
                CollectType: line.CollectType,
                Job: job),
            preferRelic: true);
    }

    private void Add(uint itemId, RelicItemTarget target, bool preferRelic = false)
    {
        if (itemId == 0)
        {
            return;
        }

        if (byItemId.TryGetValue(itemId, out RelicItemTarget? existing))
        {
            if (preferRelic && existing.Tab != RelicTrackerDestinationTab.Relic)
            {
                byItemId[itemId] = target;
            }

            return;
        }

        byItemId[itemId] = target;
    }
}

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
