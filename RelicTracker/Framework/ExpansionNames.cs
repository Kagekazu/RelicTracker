namespace RelicTracker.Framework;

internal static class ExpansionNames
{
    private static readonly string[] LongNames =
    [
        "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers",
        "Endwalker", "Dawntrail", "Crafters & Gatherers"
    ];

    public static string LongName(string expansionId) =>
        expansionId switch
        {
            "ARR" => LongNames[0],
            "HW" => LongNames[1],
            "SB" => LongNames[2],
            "ShB" => LongNames[3],
            "EW" => LongNames[4],
            "DT" => LongNames[5],
            "DoHDoL" => LongNames[6],
            _ => expansionId
        };
}
