using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System.Numerics;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI : Window
{
    private const string WindowId = "RelicTracker";

    private static readonly Vector4 HeaderColor = new(0.85f, 0.72f, 0.35f, 1f);
    private static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 GoodColor = new(0.45f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 BadColor = new(0.95f, 0.45f, 0.45f, 1f);
    private readonly RelicCatalog catalog;

    private readonly Configuration config;
    private readonly RelicDataService data;
    private readonly FfxivCollectService ffxivCollect;

    private bool trackerTabVisible;

    public PluginUI(Configuration config, RelicDataService data, RelicCatalog catalog, FfxivCollectService ffxivCollect)
        : base($"Relic Tracker###{WindowId}")
    {
        this.config = config;
        this.data = data;
        this.catalog = catalog;
        this.ffxivCollect = ffxivCollect;

        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(880, 640);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(4000, 3000),
        };

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Ko-fi (because relics are thirsty work)"),
            Click = _ => ShellStart("https://ko-fi.com/kagekazu")
        });
    }

    public override void OnClose()
    {
        config.PersistIfDirty();
        base.OnClose();
    }

    public override void Draw()
    {
        FlushInventoryCountInvalidation();
        trackerTabVisible = false;
        if (ImGui.BeginTabBar("RelicTrackerTabs"))
        {
            if (ImGui.BeginTabItem("Overview", TabOpenFlags(RelicTrackerDestinationTab.Overview)))
            {
                ConsumePendingTab(RelicTrackerDestinationTab.Overview);
                DrawOverviewTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Relic", TabOpenFlags(RelicTrackerDestinationTab.Relic)))
            {
                ConsumePendingTab(RelicTrackerDestinationTab.Relic);
                DrawRelicTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Tracker", TabOpenFlags(RelicTrackerDestinationTab.Tracker)))
            {
                ConsumePendingTab(RelicTrackerDestinationTab.Tracker);
                DrawTrackerTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawTitleBarVersion(
            TitleBarButtons.Count,
            AllowPinning || AllowClickthrough);
    }
}
