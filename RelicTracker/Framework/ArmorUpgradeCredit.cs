namespace RelicTracker.Framework;

/// <summary>
/// Higher armor tiers credit lower ones (same as weapons). Cross-set rules:
/// any Phantom Vision piece credits all Arcanaut's ranks; Augmented Law's Order
/// credits all Bozjan set tiers. Plain Law's Order stays within its own set.
/// </summary>
public static class ArmorUpgradeCredit
{
    public const string ArcanautsSet = "Arcanaut's";
    public const string PhantomVisionSet = "Phantom Vision";
    public const string BozjanSet = "Bozjan";
    public const string LawsOrderSet = "Law's Order";

    /// <summary>
    /// Marks the owned piece and every lower tier in its set, plus cross-set credits.
    /// </summary>
    public static void AddOwnedPieceKeys(
        ArmorLine line,
        ArmorSet set,
        int ownedTierIndex,
        int pieceIndex,
        HashSet<string> done)
    {
        for (var tierIndex = 0; tierIndex <= ownedTierIndex && tierIndex < set.Tiers.Count; tierIndex++)
        {
            done.Add($"{set.Tiers[tierIndex].CollectType}|{pieceIndex}");
        }

        if (string.Equals(set.Name, PhantomVisionSet, StringComparison.OrdinalIgnoreCase))
        {
            MarkAllTiers(FindSet(line, ArcanautsSet), pieceIndex, done);
            return;
        }

        if (string.Equals(set.Name, LawsOrderSet, StringComparison.OrdinalIgnoreCase)
            && IsAugmentedTier(set, ownedTierIndex))
        {
            MarkAllTiers(FindSet(line, BozjanSet), pieceIndex, done);
        }
    }

    /// <summary>
    /// True if the piece index is owned at the cost tier or any higher tier of the same set,
    /// or via cross-set credit (any Phantom Vision for Arcanaut's; Aug. Law's for Bozjan).
    /// </summary>
    public static bool PieceSatisfied(
        ArmorLine line,
        ArmorSet costSet,
        int costTierIndex,
        int pieceIndex,
        Func<uint, uint> ownedLookup)
    {
        if (OwnedAtOrAbove(costSet, costTierIndex, pieceIndex, ownedLookup))
        {
            return true;
        }

        if (string.Equals(costSet.Name, ArcanautsSet, StringComparison.OrdinalIgnoreCase))
        {
            var vision = FindSet(line, PhantomVisionSet);
            return vision is not null
                   && OwnedAtOrAbove(vision, minTierIndex: 0, pieceIndex, ownedLookup);
        }

        if (string.Equals(costSet.Name, BozjanSet, StringComparison.OrdinalIgnoreCase))
        {
            var lawsOrder = FindSet(line, LawsOrderSet);
            if (lawsOrder is null)
            {
                return false;
            }

            var augTier = FindAugmentedTierIndex(lawsOrder);
            return augTier >= 0
                   && OwnedAtOrAbove(lawsOrder, augTier, pieceIndex, ownedLookup);
        }

        return false;
    }

    private static void MarkAllTiers(ArmorSet? set, int pieceIndex, HashSet<string> done)
    {
        if (set is null)
        {
            return;
        }

        for (var tierIndex = 0; tierIndex < set.Tiers.Count; tierIndex++)
        {
            done.Add($"{set.Tiers[tierIndex].CollectType}|{pieceIndex}");
        }
    }

    private static bool IsAugmentedTier(ArmorSet set, int tierIndex) =>
        tierIndex >= 0
        && tierIndex < set.Tiers.Count
        && string.Equals(set.Tiers[tierIndex].Label, "Augmented", StringComparison.OrdinalIgnoreCase);

    private static int FindAugmentedTierIndex(ArmorSet set)
    {
        for (var i = 0; i < set.Tiers.Count; i++)
        {
            if (IsAugmentedTier(set, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static ArmorSet? FindSet(ArmorLine line, string setName)
    {
        foreach (var set in line.Sets)
        {
            if (string.Equals(set.Name, setName, StringComparison.OrdinalIgnoreCase))
            {
                return set;
            }
        }

        return null;
    }

    private static bool OwnedAtOrAbove(
        ArmorSet set,
        int minTierIndex,
        int pieceIndex,
        Func<uint, uint> ownedLookup)
    {
        if (minTierIndex < 0)
        {
            return false;
        }

        for (var tierIndex = minTierIndex; tierIndex < set.Tiers.Count; tierIndex++)
        {
            var tier = set.Tiers[tierIndex];
            if (pieceIndex < 0 || pieceIndex >= tier.PieceIds.Count)
            {
                continue;
            }

            var pieceId = tier.PieceIds[pieceIndex];
            if (pieceId != 0 && ownedLookup(pieceId) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
