namespace RelicTracker.Framework;

internal static class CollectStepMap
{
    private static readonly Dictionary<string, string[]> SectionOrderByExpansion =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ARR"] = ["A Relic Reborn", "Currencies"],
            ["HW"] = ["Anima Weapons", "Currencies"],
            ["SB"] = ["Eureka Weapons", "Currencies"],
            ["ShB"] = ["Resistance Weapons", "Currencies"],
            ["EW"] = ["Manderville Weapons", "Currencies"],
            ["DT"] = ["Phantom Weapons", "Exquisite Weapons", "Figmental Weapons", "Currencies"],
            ["DoHDoL"] = ["Skysteel Tools", "Splendorous Tools", "Cosmic Tools", "Currencies"],
        };

    private static readonly Dictionary<string, Dictionary<string, CollectStepRequirement>> ByExpansion =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ARR"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Relic"] = Req("A Relic Reborn", 0),
                ["Zenith"] = Req("A Relic Reborn", 1),
                ["Atma"] = Req("A Relic Reborn", 2),
                ["Animus"] = Req("A Relic Reborn", 3),
                ["Novus"] = Req("A Relic Reborn", 4),
                ["Nexus"] = Req("A Relic Reborn", 5),
                ["Zodiac"] = Req("A Relic Reborn", 6),
                ["Zeta"] = Req("A Relic Reborn", 7),
                ["Kettle to the Mettle"] = Req("A Relic Reborn", 7),
            },
            ["HW"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Animated"] = Req("Anima Weapons", 0),
                ["Awoken"] = Req("Anima Weapons", 1),
                ["Anima"] = Req("Anima Weapons", 2),
                ["Hyperconductive"] = Req("Anima Weapons", 3),
                ["Reconditioned"] = Req("Anima Weapons", 4),
                ["Sharpened"] = Req("Anima Weapons", 5),
                ["Complete"] = Req("Anima Weapons", 6),
                ["Lux"] = Req("Anima Weapons", 6),
            },
            ["SB"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Anemos"] = Req("Eureka Weapons", 3),
                ["Pagos"] = Req("Eureka Weapons", 4),
                ["Pyros"] = Req("Eureka Weapons", 9),
                ["Elemental"] = Req("Eureka Weapons", 6),
            },
            ["ShB"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Resistance"] = Req("Resistance Weapons", 0),
                ["Augmented"] = Req("Resistance Weapons", 1),
                ["Recollection"] = Req("Resistance Weapons", 2),
                ["Law's Order"] = Req("Resistance Weapons", 3),
                ["Augmented Law's"] = Req("Resistance Weapons", 4),
                ["Blade's"] = Req("Resistance Weapons", 5),
            },
            ["EW"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Manderville"] = Req("Manderville Weapons", 0),
                ["Amazing"] = Req("Manderville Weapons", 1),
                ["Majestic"] = Req("Manderville Weapons", 2),
                ["Mandervillous"] = Req("Manderville Weapons", 3),
            },
            ["DT"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Penumbrae"] = Req("Phantom Weapons", 0),
                ["Umbrae"] = Req("Phantom Weapons", 1),
                ["Obscurum"] = Req("Phantom Weapons", 2),
                ["Arcanaut"] = Req("Exquisite Weapons", 0),
                ["Step 4"] = Req("Figmental Weapons", 0),
            },
            ["DoHDoL"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Dragonsung"] = Req("Skysteel Tools", 2),
                ["Skysung"] = Req("Skysteel Tools", 4),
                ["Skybuilders"] = Req("Skysteel Tools", 5),
                ["Crystalline"] = Req("Splendorous Tools", 2),
                ["Brilliant"] = Req("Splendorous Tools", 4),
                ["Lodestar"] = Req("Splendorous Tools", 6),
                ["Cosmic"] = Req("Cosmic Tools", 0),
                ["Stellar"] = Req("Cosmic Tools", 1),
                ["Hyper"] = Req("Cosmic Tools", 2),
            },
        };

    public static IReadOnlyList<string> GetSectionOrder(string expansionId)
    {
        if (SectionOrderByExpansion.TryGetValue(expansionId, out var order))
        {
            return order;
        }

        return ["Materials", "Currencies"];
    }

    public static string ResolveSection(string expansionId, string? step, bool isCurrency)
    {
        if (isCurrency)
        {
            return "Currencies";
        }

        if (!string.IsNullOrWhiteSpace(step)
            && TryGetRequirement(expansionId, step, out var requirement))
        {
            return requirement.CollectType;
        }

        if (SectionOrderByExpansion.TryGetValue(expansionId, out var order) && order.Length > 0)
        {
            return order[0];
        }

        return "Materials";
    }

    public static bool TryGetRequirement(string expansionId, string step, out CollectStepRequirement requirement)
    {
        if (ByExpansion.TryGetValue(expansionId, out var steps)
            && steps.TryGetValue(step, out requirement))
        {
            return true;
        }

        requirement = default;
        return false;
    }

    private static CollectStepRequirement Req(string collectType, int tier) =>
        new(collectType, tier);
}

internal readonly struct CollectStepRequirement
{
    public CollectStepRequirement(string collectType, int requiredTier)
    {
        CollectType = collectType;
        RequiredTier = requiredTier;
    }

    public string CollectType { get; }

    public int RequiredTier { get; }
}
