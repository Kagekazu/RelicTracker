using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using RelicTracker.IPC;
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

    // Progress fill (green once complete) and "still short" amounts: calm colours for normal state,
    // so yellow/red keep meaning a real problem.
    private static readonly Vector4 ProgressColor = new(0.38f, 0.66f, 0.88f, 1f);
    private static readonly Vector4 ShortColor = new(0.9f, 0.68f, 0.45f, 1f);
    private readonly RelicCatalog catalog;

    private readonly Configuration config;
    private readonly RelicDataService data;
    private readonly FfxivCollectService ffxivCollect;

    private bool trackerTabVisible;
    private enum Page { Collection, Upgrade, Materials, Setup }
    private Page page;
    private Page pageBeforeSetup;

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
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog,
            ShowTooltip = () => ImGui.SetTooltip(page == Page.Setup ? "Close Setup" : "Setup"),
            Click = _ => ToggleSetup()
        });
    }

    public override void PreDraw() => RelicTheme.Push();

    public override void PostDraw() => RelicTheme.Pop();

    public override void OnClose()
    {
        page = Page.Collection;
        inspectedTier = null;
        armorSelection = null;
        collectionSearch = string.Empty;
        collectionScroll = 0;
        restoreCollectionScroll = true;
        config.PersistIfDirty();
        base.OnClose();
    }

    public override void Draw()
    {
        FlushInventoryCountInvalidation();
        trackerTabVisible = false;
        DrawPageButton("All Relics", Page.Collection);
        ImGui.SameLine();
        DrawPageButton("Materials", Page.Materials);
        ImGui.Separator();

        switch (page)
        {
            case Page.Collection:
                DrawCollection();
                break;
            case Page.Upgrade:
                DrawRelicTab();
                break;
            case Page.Materials:
                if (MogVaultIpc.IsReady)
                    DrawTrackerTab();
                else
                    DrawMogVaultBlocker();
                break;
            case Page.Setup:
                DrawSettingsTab();
                break;
        }

        DrawTitleBarVersion(
            TitleBarButtons.Count,
            AllowPinning || AllowClickthrough);
    }

    private void DrawPageButton(string label, Page destination)
    {
        bool active = page == destination || (page == Page.Upgrade && destination == Page.Collection);
        using var color = ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive], active);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        bool clicked = ImGui.Button(label);
        ImGui.PopStyleVar();
        if (clicked)
        {
            restoreCollectionScroll = destination == Page.Collection && page != Page.Collection;
            page = destination;
        }
    }

    private void ToggleSetup()
    {
        if (page == Page.Setup)
        {
            page = pageBeforeSetup;
            restoreCollectionScroll = page == Page.Collection;
            return;
        }

        pageBeforeSetup = page;
        page = Page.Setup;
    }
}
