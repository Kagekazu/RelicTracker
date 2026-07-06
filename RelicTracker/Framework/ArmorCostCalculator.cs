namespace RelicTracker.Framework;

/// <summary>Credits owned relic armor pieces toward Tracker currency needs (spent mats).</summary>
public static class ArmorCostCalculator
{
    private static readonly Dictionary<string, (string SetName, string TierKey, int? Slot)> CostLinks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Artifact (base)"] = ("Eurekan", "Base", null),
            ["Artifact +1"] = ("Eurekan", "+1", null),
            ["Artifact +2"] = ("Eurekan", "+2", null),
            ["Anemos (final)"] = ("Eurekan", "Anemos", null),
            ["Elemental (base)"] = ("Elemental", "Base", null),
            ["Elemental +1"] = ("Elemental", "+1", null),
            ["Elemental +2"] = ("Elemental", "+2", null),
            ["Bozjan"] = ("Bozjan", "Base", null),
            ["Augmented Bozjan"] = ("Bozjan", "Augmented", null),
            ["Law's Order"] = ("Law's Order", "Base", null),
            ["Aug. Law's Order (Head)"] = ("Law's Order", "Augmented", 0),
            ["Aug. Law's Order (Body)"] = ("Law's Order", "Augmented", 1),
            ["Aug. Law's Order (Hands)"] = ("Law's Order", "Augmented", 2),
            ["Aug. Law's Order (Legs)"] = ("Law's Order", "Augmented", 3),
            ["Aug. Law's Order (Feet)"] = ("Law's Order", "Augmented", 4),
            ["Blade's"] = ("Blade's", "Base", null),
            ["Arcanaut's (base)"] = ("Arcanaut's", "Base", null),
            ["Arcanaut's +1"] = ("Arcanaut's", "+1", null),
            ["Arcanaut's +2"] = ("Arcanaut's", "+2", null),
        };

    public static uint ArmorPieceCredit(
        string expansionId,
        ArmorCostRow cost,
        RelicCatalog catalog,
        Func<uint, uint> ownedLookup)
    {
        if (!TryResolveTier(expansionId, cost.Set, catalog, out ArmorTier tier, out int? slotFilter))
        {
            return 0;
        }

        var credit = 0u;
        var pieceCount = Math.Min(tier.Pieces, tier.PieceIds.Count);
        for (var index = 0; index < pieceCount; index++)
        {
            var slot = index % 5;
            if (slotFilter is int requiredSlot && slot != requiredSlot)
            {
                continue;
            }

            var pieceId = tier.PieceIds[index];
            if (pieceId == 0 || ownedLookup(pieceId) == 0)
            {
                continue;
            }

            credit += CreditPerPiece(cost, slot);
        }

        return credit;
    }

    private static bool TryResolveTier(
        string expansionId,
        string? costSet,
        RelicCatalog catalog,
        out ArmorTier tier,
        out int? slotFilter)
    {
        tier = null!;
        slotFilter = null;
        if (string.IsNullOrWhiteSpace(costSet) || !CostLinks.TryGetValue(costSet.Trim(), out var link))
        {
            return false;
        }

        slotFilter = link.Slot;
        foreach (var line in catalog.ArmorLines)
        {
            if (!string.Equals(line.Expansion, expansionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var set in line.Sets)
            {
                if (!string.Equals(set.Name, link.SetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var candidate in set.Tiers)
                {
                    if (TierMatches(candidate, link.TierKey))
                    {
                        tier = candidate;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TierMatches(ArmorTier tier, string tierKey) =>
        string.Equals(tier.Label, tierKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tier.CollectType, tierKey, StringComparison.OrdinalIgnoreCase);

    private static uint CreditPerPiece(ArmorCostRow cost, int slotInSet)
    {
        if (cost.SetTotal == cost.PerPiece * 5)
        {
            return (uint)cost.PerPiece;
        }

        var bodyLegs = (uint)cost.PerPiece;
        var other = (uint)((cost.SetTotal - (2 * cost.PerPiece)) / 3);
        return slotInSet is 1 or 3 ? bodyLegs : other;
    }
}
