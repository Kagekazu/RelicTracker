using RelicTracker.IPC;
using System.Numerics;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private enum ProgressHintContext
    {
        Overview,
        Tracker,
        RelicDisconnected,
    }

    private static readonly string[] ExpansionLongNames =
    [
        "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers",
        "Endwalker", "Dawntrail", "Crafters & Gatherers"
    ];

    private const long InventoryCacheBucketMs = 10_000;

    private bool CollectIdLinked => config.FfxivCollectCharacterId != 0;

    private static long InventoryCacheStamp() =>
        AllaganToolsIpc.IsReady ? Environment.TickCount64 / InventoryCacheBucketMs : 0;

    private Func<uint, uint> CreateOwnedLookup()
    {
        long stamp = InventoryCacheStamp();
        if (ownedCountCache is null || ownedCountCacheStamp != stamp)
        {
            ownedCountCache = new Dictionary<uint, uint>();
            ownedCountCacheStamp = stamp;
        }

        Dictionary<uint, uint> cache = ownedCountCache;
        return itemId =>
        {
            if (!cache.TryGetValue(itemId, out uint count))
            {
                count = AllaganToolsIpc.GetOwnedCount(itemId, activeCharacterOnly: true);
                cache[itemId] = count;
            }

            return count;
        };
    }

    private void InvalidateOwnedCountCache()
    {
        ownedCountCache = null;
        ownedCountCacheStamp = 0;
    }

    private void DrawProgressSourceHint(ProgressHintContext context)
    {
        bool inventory = AllaganToolsIpc.IsReady;
        bool collect = CollectIdLinked;

        switch (context)
        {
            case ProgressHintContext.Overview when !collect:
                ImGui.TextColored(
                    MutedColor,
                    inventory
                        ? "Owned relics are auto-tracked from Allagan Tools (replicas count). Tick any missing steps on the Relic tab."
                        : "Tick steps on the Relic tab to fill this in. Connect Allagan Tools on Settings for owned-relic detection.");
                ImGui.Spacing();
                break;
            case ProgressHintContext.Tracker when !inventory && !collect:
                ImGui.TextColored(
                    MutedColor,
                    "Tick finished steps on the Relic tab to trim this list. Connect Allagan Tools to auto-fill owned relics (replicas count).");
                ImGui.Spacing();
                break;
            case ProgressHintContext.RelicDisconnected when !inventory && !collect:
                ImGui.TextColored(
                    MutedColor,
                    "Tick steps manually, or connect Allagan Tools on Settings to auto-fill steps for relics (and replicas) you still own.");
                break;
        }
    }

    private static string DescribeWeaponProgressSource(bool inventoryLinked, bool collectLinked)
    {
        if (inventoryLinked && collectLinked)
        {
            return "Auto-tracked from Allagan Tools (replicas count). FFXIV Collect fills in steps you no longer have in inventory.";
        }

        if (inventoryLinked)
        {
            return "Auto-tracked from Allagan Tools inventory (replicas count too).";
        }

        return "Auto-tracked from FFXIV Collect — for relics no longer in inventory.";
    }

    private static string DescribeArmorProgressSource(bool inventoryLinked, bool collectLinked)
    {
        if (inventoryLinked && collectLinked)
        {
            return "Auto-tracked from Allagan Tools. FFXIV Collect fills in pieces you no longer have in inventory.";
        }

        if (inventoryLinked)
        {
            return "Auto-tracked from Allagan Tools inventory.";
        }

        return "Auto-tracked from FFXIV Collect — for pieces no longer in inventory.";
    }

    private void DrawPluginConnectionStatus(string label, bool installed, bool enabled, bool ready)
    {
        if (!installed)
        {
            ImGui.TextColored(string.Equals(label, "Artisan", StringComparison.Ordinal) ? MutedColor : WarningColor,
                $"{label} is not installed.");
            return;
        }

        if (!enabled)
        {
            ImGui.TextColored(WarningColor, $"{label} is installed but not enabled.");
            return;
        }

        if (!ready)
        {
            ImGui.TextColored(WarningColor, label == "Artisan"
                ? "Artisan found — relic craft lists need a newer Artisan build."
                : $"{label} is loading inventory data…");
            return;
        }

        ImGui.TextColored(GoodColor, $"{label} connected");
    }

    private void DrawPercentBar(float fraction, float width, string overlay)
    {
        Vector4 color = fraction >= 1f ? GoodColor : fraction > 0f ? WarningColor : MutedColor;
        using var barColor = ImRaii.PushColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new Vector2(width, ImGui.GetFrameHeight()), overlay);
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
