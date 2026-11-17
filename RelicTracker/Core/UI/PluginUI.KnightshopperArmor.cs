using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private List<KnightshopperPurchase> GetPhantomArmorPurchases(
        ArmorLine armor, ArmorSet set, ArmorTier tier, RelicOwnership ownership, Func<uint, uint> ownedLookup)
    {
        var missing = Enumerable.Range(0, tier.Pieces)
            .Where(index => !ownership.IsArmorPieceOwned(tier.CollectType, index))
            .ToArray();
        if (missing.Length == 0)
        {
            return [];
        }

        return tier.Label == "Base"
            ? PhantomBasePiecePurchases(tier, missing)
            : PhantomUpgradeMaterialPurchases(armor, set, tier, missing, ownedLookup);
    }

    private void DrawPhantomArmorShoppingButton(ArmorSet set, ArmorTier tier, IReadOnlyList<KnightshopperPurchase> purchases)
    {
        DrawKnightshopperSaveButton(KnightshopperCurrency.OccultCrescent, purchases, $"{set.Name} {tier.Label}",
            tier.Label == "Base" ? "Save missing pieces to Knightshopper" : "Save upgrade materials to Knightshopper");
    }

    private static List<KnightshopperPurchase> PhantomBasePiecePurchases(ArmorTier tier, IReadOnlyList<int> missing) =>
    [
        .. missing.Where(index => index < tier.PieceIds.Count)
            .Select(index => tier.PieceIds[index])
            .Where(id => id != 0)
            .Distinct()
            .Select(id => new KnightshopperPurchase(id, ItemDisplayNames.Resolve(id, $"Item {id}"), 1)),
    ];

    private List<KnightshopperPurchase> PhantomUpgradeMaterialPurchases(
        ArmorLine armor, ArmorSet set, ArmorTier tier, IReadOnlyList<int> missing, Func<uint, uint> ownedLookup)
    {
        if (!data.ArmorCosts.TryGetValue(armor.Expansion, out var costs))
        {
            return [];
        }

        List<KnightshopperPurchase> purchases = [];
        foreach (var cost in costs)
        {
            uint itemId = cost.CurrencyIds.FirstOrDefault();
            // These materials have direct offers for Knightshopper's supported Occult Crescent currencies.
            if (itemId is not (47864 or 47865 or 51978)
                || !ArmorCostCalculator.CostAppliesTo(cost, set.Name, tier, null))
            {
                continue;
            }

            uint need = missing.Aggregate(0u, (sum, piece) =>
                sum + ArmorCostCalculator.PieceCost(cost, piece % ArmorCostCalculator.PiecesPerSet));
            uint owned = ownedLookup(itemId);
            if (owned < need)
            {
                purchases.Add(new(itemId, ItemDisplayNames.Resolve(itemId, cost.Currency), need - owned));
            }
        }
        return purchases;
    }
}
