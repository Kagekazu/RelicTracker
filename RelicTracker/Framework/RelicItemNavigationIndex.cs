namespace RelicTracker.Framework;

using Dalamud.Game.Inventory;

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

/// <summary>Maps item row IDs to the Relic Tracker tab/expansion to open from a context menu.</summary>
public sealed class RelicItemNavigationIndex
{
    private static readonly string[] ExpansionLongNames =
    [
        "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers",
        "Endwalker", "Dawntrail", "Crafters & Gatherers"
    ];

    private readonly Dictionary<uint, RelicItemTarget> byItemId = [];

    public RelicItemNavigationIndex(RelicDataService data, RelicCatalog catalog)
    {
        IndexMaterials(data);
        IndexArmorCurrency(data);
        IndexArmorPieces(catalog);
        IndexRelics(catalog);
        Svc.Log.Information("[RelicTracker] Indexed {Count} relic-related item IDs for context menu.", byItemId.Count);
    }

    public bool TryGet(uint itemId, out RelicItemTarget target) =>
        byItemId.TryGetValue(itemId, out target!);

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
                    : $"Open Tracker — {ExpansionLongName(expansionId)}";

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
                    : $"Open Tracker — {ExpansionLongName(expansionId)}";
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
        var label = $"Open Relic — {line.CollectType}";
        Add(
            itemId,
            new RelicItemTarget(
                RelicTrackerDestinationTab.Relic,
                line.Expansion,
                label,
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

    private static string ExpansionLongName(string expansionId) =>
        expansionId switch
        {
            "ARR" => ExpansionLongNames[0],
            "HW" => ExpansionLongNames[1],
            "SB" => ExpansionLongNames[2],
            "ShB" => ExpansionLongNames[3],
            "EW" => ExpansionLongNames[4],
            "DT" => ExpansionLongNames[5],
            "DoHDoL" => ExpansionLongNames[6],
            _ => expansionId
        };
}
