namespace RelicTracker.Framework;

/// <summary>
/// Higher armor tiers credit lower ones (same as weapons). Phantom Vision also credits
/// matching/lower Arcanaut's tiers — same role gear at higher ilvl, nothing locked behind Arcanaut's.
/// </summary>
public static class ArmorUpgradeCredit
{
    public const string ArcanautsSet = "Arcanaut's";
    public const string PhantomVisionSet = "Phantom Vision";

    /// <summary>
    /// Marks the owned piece and every lower tier in its set. Phantom Vision also marks
    /// Arcanaut's at the same piece index up through the matching upgrade rank.
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

        if (!string.Equals(set.Name, PhantomVisionSet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var arcanauts = FindSet(line, ArcanautsSet);
        if (arcanauts is null)
        {
            return;
        }

        var maxTier = Math.Min(ownedTierIndex, arcanauts.Tiers.Count - 1);
        for (var tierIndex = 0; tierIndex <= maxTier; tierIndex++)
        {
            done.Add($"{arcanauts.Tiers[tierIndex].CollectType}|{pieceIndex}");
        }
    }

    /// <summary>
    /// True if the piece index is owned at the cost tier or any higher tier of the same set,
    /// or (for Arcanaut's costs) at that rank or higher on Phantom Vision.
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

        if (!string.Equals(costSet.Name, ArcanautsSet, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vision = FindSet(line, PhantomVisionSet);
        return vision is not null
               && OwnedAtOrAbove(vision, costTierIndex, pieceIndex, ownedLookup);
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
