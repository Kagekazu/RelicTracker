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

    private enum StatusChipKind
    {
        Ok,
        Warn,
        Muted,
    }

    private const long InventoryCacheBucketMs = 10_000;
    private const long TrackerInventoryRefreshMs = 500;
    private const float RelicWideLayoutMinWidth = 820f;

    private static readonly Vector4 PanelBg = new(0.10f, 0.10f, 0.12f, 0.55f);
    private static readonly Vector4 PanelBorder = new(0.40f, 0.40f, 0.45f, 0.55f);
    private static readonly Vector4 ChipOkBg = new(0.18f, 0.38f, 0.24f, 0.95f);
    private static readonly Vector4 ChipWarnBg = new(0.42f, 0.32f, 0.10f, 0.95f);
    private static readonly Vector4 ChipMutedBg = new(0.22f, 0.22f, 0.25f, 0.95f);

    private const float PanelPadX = 12f;
    private const float PanelPadY = 10f;

    private readonly Stack<PanelScope> panelStack = new();

    private readonly struct PanelScope
    {
        public required bool UseChild { get; init; }
        public float Width { get; init; }
    }

    private bool CollectIdLinked => config.FfxivCollectCharacterId != 0;

    private static long InventoryCacheStamp() =>
        AllaganToolsIpc.IsReady ? Environment.TickCount64 / InventoryCacheBucketMs : 0;

    private long OwnedCountRefreshStamp()
    {
        long interval = trackerTabVisible ? TrackerInventoryRefreshMs : InventoryCacheBucketMs;
        return Environment.TickCount64 / interval;
    }

    private Func<uint, uint> CreateOwnedLookup()
    {
        long stamp = OwnedCountRefreshStamp();
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
                count = PlayerInventory.GetItemCount(itemId);
                uint allagan = AllaganToolsIpc.GetOwnedCount(itemId, activeCharacterOnly: true);
                if (allagan > count)
                {
                    count = allagan;
                }

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

    private void DrawTabIntro(string blurb)
    {
        ImGui.TextColored(MutedColor, blurb);
        ImGui.Dummy(new Vector2(0, 4));
    }

    private void EndStickyHeader()
    {
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));
    }

    /// <summary>
    /// Bordered content card. Height 0 (default) sizes to content — do not use BeginChild(0)
    /// which fills the rest of the window and leaves a huge empty region.
    /// Pass a non-zero height only when you intentionally want a fixed/fill child.
    /// </summary>
    private bool BeginPanel(string id, float height = 0f)
    {
        if (height != 0f)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(PanelPadX, PanelPadY));
            if (!ImGui.BeginChild($"##panel_{id}", new Vector2(0, height), true))
            {
                ImGui.EndChild();
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor();
                return false;
            }

            panelStack.Push(new PanelScope { UseChild = true });
            return true;
        }

        ImGui.PushID(id);
        float width = ImGui.GetContentRegionAvail().X;
        panelStack.Push(new PanelScope { UseChild = false, Width = width });

        // Draw background behind content after we know the group height.
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width, PanelPadY));
        ImGui.Indent(PanelPadX);
        return true;
    }

    private void EndPanel()
    {
        PanelScope scope = panelStack.Pop();
        if (scope.UseChild)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 8));
            return;
        }

        ImGui.Unindent(PanelPadX);
        ImGui.Dummy(new Vector2(scope.Width, PanelPadY));
        ImGui.EndGroup();

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        max.X = min.X + scope.Width;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(PanelBg), 5f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(PanelBorder), 5f);
        drawList.ChannelsMerge();

        ImGui.PopID();
        ImGui.Dummy(new Vector2(0, 8));
    }

    private void DrawStatusChip(string label, StatusChipKind kind)
    {
        Vector4 bg = kind switch
        {
            StatusChipKind.Ok => ChipOkBg,
            StatusChipKind.Warn => ChipWarnBg,
            _ => ChipMutedBg
        };
        Vector4 fg = kind switch
        {
            StatusChipKind.Ok => GoodColor,
            StatusChipKind.Warn => WarningColor,
            _ => MutedColor
        };

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleColor(ImGuiCol.Text, fg);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 11f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 3f));
        ImGui.SmallButton(label);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
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
                        ? "Owned relics fill in from inventory (Allagan Tools). Tick any missing steps on Relic."
                        : "Tick steps on Relic to fill this in. Connect Allagan Tools in Settings for owned-relic detection.");
                ImGui.Spacing();
                break;
            case ProgressHintContext.Tracker when !inventory && !collect:
                ImGui.TextColored(
                    MutedColor,
                    "Tick finished steps on Relic to trim this list. Connect Allagan Tools in Settings to detect owned relics.");
                ImGui.Spacing();
                break;
            case ProgressHintContext.RelicDisconnected when !inventory && !collect:
                ImGui.TextColored(
                    MutedColor,
                    "Tick steps manually, or connect Allagan Tools in Settings to auto-fill owned relics.");
                break;
        }
    }

    private static string DescribeWeaponProgressSource(bool inventoryLinked, bool collectLinked) =>
        DescribeProgressSource(inventoryLinked, collectLinked, "Steps", "relics");

    private static string DescribeArmorProgressSource(bool inventoryLinked, bool collectLinked) =>
        DescribeProgressSource(inventoryLinked, collectLinked, "Pieces", "pieces");

    private static string DescribeProgressSource(
        bool inventoryLinked,
        bool collectLinked,
        string fillNoun,
        string orphanNoun)
    {
        if (inventoryLinked && collectLinked)
        {
            return $"{fillNoun} fill from inventory (Allagan Tools). Collect covers sold or desynthed {orphanNoun}.";
        }

        if (inventoryLinked)
        {
            return $"{fillNoun} fill from inventory (Allagan Tools).";
        }

        return $"{fillNoun} fill from FFXIV Collect — for {orphanNoun} no longer in inventory.";
    }

    private void DrawPluginConnectionStatus(string label, bool installed, bool enabled, bool ready)
    {
        if (!installed)
        {
            DrawStatusChip(
                "Not installed",
                string.Equals(label, "Artisan", StringComparison.Ordinal) ? StatusChipKind.Muted : StatusChipKind.Warn);
            ImGui.SameLine();
            ImGui.TextColored(
                string.Equals(label, "Artisan", StringComparison.Ordinal) ? MutedColor : WarningColor,
                $"{label} is not installed.");
            return;
        }

        if (!enabled)
        {
            DrawStatusChip("Disabled", StatusChipKind.Warn);
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, $"{label} is installed but not enabled.");
            return;
        }

        if (!ready)
        {
            DrawStatusChip("Loading", StatusChipKind.Warn);
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, label == "Artisan"
                ? "Artisan found — relic craft lists need a newer Artisan build."
                : $"{label} is loading inventory data…");
            return;
        }

        DrawStatusChip("Connected", StatusChipKind.Ok);
        ImGui.SameLine();
        ImGui.TextColored(GoodColor, $"{label} connected");
    }

    private void DrawPercentBar(float fraction, float width, string overlay)
    {
        Vector4 color = fraction >= 1f ? GoodColor : fraction > 0f ? WarningColor : MutedColor;
        using var barColor = ImRaii.PushColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new Vector2(width, ImGui.GetFrameHeight()), overlay);
    }
}
