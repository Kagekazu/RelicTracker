using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

/// <summary>
///     Overlays armor currency IDs and per-piece amounts from <see cref="SpecialShop"/>.
///     Bundled JSON stays the fallback when a listing isn't in the shop sheet (or costs vary by slot).
/// </summary>
internal static class ArmorShopResolver
{
    public static void Apply(RelicDataService data, RelicCatalog catalog)
    {
        try
        {
            ApplyCore(data, catalog);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] SpecialShop overlay failed; using bundled armor costs.");
        }
    }

    private static void ApplyCore(RelicDataService data, RelicCatalog catalog)
    {
        HashSet<uint> catalogPieces = CatalogPieceIds(catalog);
        if (catalogPieces.Count == 0)
        {
            return;
        }

        Dictionary<uint, List<ShopListing>> listings = IndexArmorListings(catalogPieces);
        if (listings.Count == 0)
        {
            return;
        }

        var updated = 0;
        foreach ((string expansionId, List<ArmorCostRow> rows) in data.ArmorCosts)
        {
            List<(ArmorCostRow Row, ArmorLine Line, ArmorSet Set, ArmorTier Tier, int? Slot)> resolved = [];
            foreach (var row in rows)
            {
                if (!ArmorCostCalculator.TryResolveCostTarget(
                        expansionId,
                        row.Set,
                        catalog,
                        out ArmorLine line,
                        out ArmorSet set,
                        out int tierIndex,
                        out int? slotFilter))
                {
                    continue;
                }

                resolved.Add((row, line, set, set.Tiers[tierIndex], slotFilter));
            }

            foreach (var group in resolved.GroupBy(entry => (entry.Line, entry.Set, entry.Tier, entry.Slot)))
            {
                updated += OverlayTier(group.ToList(), catalogPieces, listings);
            }
        }

        Svc.Log.Information("[RelicTracker] Overlaid {Count} armor cost rows from SpecialShop.", updated);
    }

    private static int OverlayTier(
        List<(ArmorCostRow Row, ArmorLine Line, ArmorSet Set, ArmorTier Tier, int? Slot)> rows,
        HashSet<uint> catalogPieces,
        Dictionary<uint, List<ShopListing>> listings)
    {
        List<(uint ItemId, uint Amount, string Name)> materials = ShopMaterials(rows[0].Tier, rows[0].Slot, catalogPieces, listings);
        if (materials.Count == 0)
        {
            return 0;
        }

        HashSet<uint> claimed = [];
        var updated = 0;
        List<ArmorCostRow> unmatched = [];
        foreach ((ArmorCostRow row, _, _, _, _) in rows)
        {
            if (TryMatch(row, materials, claimed, out var material))
            {
                if (ApplyMaterial(row, material))
                {
                    updated++;
                }
            }
            else
            {
                unmatched.Add(row);
            }
        }

        List<(uint ItemId, uint Amount, string Name)> leftover =
        [
            .. materials.Where(material => !claimed.Contains(material.ItemId))
        ];
        if (unmatched.Count == 1 && leftover.Count == 1 && ApplyMaterial(unmatched[0], leftover[0]))
        {
            updated++;
        }

        return updated;
    }

    private static bool TryMatch(
        ArmorCostRow row,
        List<(uint ItemId, uint Amount, string Name)> materials,
        HashSet<uint> claimed,
        out (uint ItemId, uint Amount, string Name) material)
    {
        foreach (var candidate in materials)
        {
            if (claimed.Contains(candidate.ItemId))
            {
                continue;
            }

            if (row.CurrencyIds.Contains(candidate.ItemId)
                || string.Equals(row.Currency, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                claimed.Add(candidate.ItemId);
                material = candidate;
                return true;
            }
        }

        material = default;
        return false;
    }

    private static bool ApplyMaterial(ArmorCostRow row, (uint ItemId, uint Amount, string Name) material)
    {
        var changed = false;
        if (row.CurrencyIds.Count != 1 || row.CurrencyIds[0] != material.ItemId)
        {
            row.CurrencyIds = [material.ItemId];
            changed = true;
        }

        if (!string.IsNullOrEmpty(material.Name)
            && !string.Equals(row.Currency, material.Name, StringComparison.OrdinalIgnoreCase))
        {
            row.Currency = material.Name;
            changed = true;
        }

        if (material.Amount > 0 && row.PerPiece != (int)material.Amount)
        {
            if (row.PerPiece > 0 && row.SetTotal == row.PerPiece * 5)
            {
                row.SetTotal = (int)material.Amount * 5;
            }

            if (row.PerPiece > 0 && row.AllTotal == row.PerPiece * 35)
            {
                row.AllTotal = (int)material.Amount * 35;
            }

            row.PerPiece = (int)material.Amount;
            changed = true;
        }

        return changed;
    }

    private static List<(uint ItemId, uint Amount, string Name)> ShopMaterials(
        ArmorTier tier,
        int? slotFilter,
        HashSet<uint> catalogPieces,
        Dictionary<uint, List<ShopListing>> listings)
    {
        Dictionary<uint, HashSet<uint>> amountsByItem = [];
        var count = Math.Min(tier.Pieces, tier.PieceIds.Count);
        for (var i = 0; i < count; i++)
        {
            if (slotFilter is int requiredSlot && i % 5 != requiredSlot)
            {
                continue;
            }

            uint pieceId = tier.PieceIds[i];
            if (pieceId == 0 || !listings.TryGetValue(pieceId, out var pieceListings))
            {
                continue;
            }

            foreach (var listing in pieceListings)
            {
                foreach ((uint itemId, uint amount) in listing.Costs)
                {
                    if (catalogPieces.Contains(itemId) || amount == 0)
                    {
                        continue;
                    }

                    if (!amountsByItem.TryGetValue(itemId, out var amounts))
                    {
                        amounts = [];
                        amountsByItem[itemId] = amounts;
                    }

                    amounts.Add(amount);
                }
            }
        }

        List<(uint ItemId, uint Amount, string Name)> materials = [];
        foreach ((uint itemId, HashSet<uint> amounts) in amountsByItem)
        {
            if (amounts.Count != 1)
            {
                continue;
            }

            var name = GameSheets.English<Item>().GetRowOrDefault(itemId)?.Name.ToString().Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            materials.Add((itemId, amounts.First(), name));
        }

        return materials;
    }

    private static Dictionary<uint, List<ShopListing>> IndexArmorListings(HashSet<uint> catalogPieces)
    {
        Dictionary<uint, List<ShopListing>> listings = [];
        foreach (SpecialShop shop in Svc.Data.GetExcelSheet<SpecialShop>())
        {
            foreach (var slot in shop.Item)
            {
                if (slot.ReceiveItems.Count == 0)
                {
                    continue;
                }

                uint received = slot.ReceiveItems[0].Item.RowId;
                if (received == 0 || !catalogPieces.Contains(received))
                {
                    continue;
                }

                List<(uint ItemId, uint Amount)> costs = [];
                foreach (var cost in slot.ItemCosts)
                {
                    uint itemId = cost.ItemCost.RowId;
                    if (itemId == 0 || cost.CurrencyCost == 0)
                    {
                        continue;
                    }

                    costs.Add((itemId, cost.CurrencyCost));
                }

                if (costs.Count == 0)
                {
                    continue;
                }

                if (!listings.TryGetValue(received, out var list))
                {
                    list = [];
                    listings[received] = list;
                }

                list.Add(new ShopListing(costs));
            }
        }

        return listings;
    }

    private static HashSet<uint> CatalogPieceIds(RelicCatalog catalog)
    {
        HashSet<uint> ids = [];
        foreach (ArmorLine line in catalog.ArmorLines)
        {
            foreach (ArmorTier tier in line.AllTiers)
            {
                foreach (uint pieceId in tier.PieceIds)
                {
                    if (pieceId != 0)
                    {
                        ids.Add(pieceId);
                    }
                }
            }
        }

        return ids;
    }

    private readonly record struct ShopListing(List<(uint ItemId, uint Amount)> Costs);
}
