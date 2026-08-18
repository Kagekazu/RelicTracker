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
            ["Phantom Vision (base)"] = ("Phantom Vision", "Base", null),
            ["Phantom Vision +1"] = ("Phantom Vision", "+1", null),
            ["Phantom Vision +2"] = ("Phantom Vision", "+2", null),
            ["Phantom Vision +3"] = ("Phantom Vision", "+3", null),
        };

    public static uint ArmorPieceCredit(
        string expansionId,
        ArmorCostRow cost,
        RelicCatalog catalog,
        Func<uint, uint> ownedLookup)
    {
        if (!TryResolveCostTarget(
                expansionId,
                cost.Set,
                catalog,
                out ArmorLine line,
                out ArmorSet set,
                out int tierIndex,
                out int? slotFilter))
        {
            return 0;
        }

        var credit = 0u;
        var pieceCount = set.Tiers[tierIndex].Pieces;
        for (var index = 0; index < pieceCount; index++)
        {
            var slot = index % 5;
            if (slotFilter is int requiredSlot && slot != requiredSlot)
            {
                continue;
            }

            if (!ArmorUpgradeCredit.PieceSatisfied(line, set, tierIndex, index, ownedLookup))
            {
                continue;
            }

            credit += CreditPerPiece(cost, slot);
        }

        return credit;
    }

    public static bool TryResolveCostTarget(
        string expansionId,
        string? costSet,
        RelicCatalog catalog,
        out ArmorLine line,
        out ArmorSet set,
        out int tierIndex,
        out int? slotFilter)
    {
        line = null!;
        set = null!;
        tierIndex = -1;
        slotFilter = null;
        if (string.IsNullOrWhiteSpace(costSet) || !CostLinks.TryGetValue(costSet.Trim(), out var link))
        {
            return false;
        }

        slotFilter = link.Slot;
        foreach (var candidateLine in catalog.ArmorLines)
        {
            if (!string.Equals(candidateLine.Expansion, expansionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var candidateSet in candidateLine.Sets)
            {
                if (!string.Equals(candidateSet.Name, link.SetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var i = 0; i < candidateSet.Tiers.Count; i++)
                {
                    if (!TierMatches(candidateSet.Tiers[i], link.TierKey))
                    {
                        continue;
                    }

                    line = candidateLine;
                    set = candidateSet;
                    tierIndex = i;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool CostAppliesTo(ArmorCostRow cost, string setName, ArmorTier tier, int? pieceIndex)
    {
        if (string.IsNullOrWhiteSpace(cost.Set) || !CostLinks.TryGetValue(cost.Set.Trim(), out var link))
        {
            return false;
        }

        if (!string.Equals(link.SetName, setName, StringComparison.OrdinalIgnoreCase)
            || !TierMatches(tier, link.TierKey))
        {
            return false;
        }

        if (pieceIndex is int index && link.Slot is int requiredSlot && index % 5 != requiredSlot)
        {
            return false;
        }

        return true;
    }

    public static uint PieceCost(ArmorCostRow cost, int slotInSet) => CreditPerPiece(cost, slotInSet);

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
