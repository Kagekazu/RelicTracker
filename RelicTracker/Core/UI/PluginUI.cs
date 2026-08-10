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

    private string materialFilter = string.Empty;
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

        TitleBarVersion.DrawFromContext(
            TitleBarButtons.Count,
            AllowPinning || AllowClickthrough);
    }

    private void DrawTrackerTab()
    {
        trackerTabVisible = true;
        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));

        DrawTabIntro("Shopping list for unfinished jobs. Open Relic for per-job steps and notes.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Expansion");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##expansion-tracker", ExpansionNames.LongName(config.SelectedExpansionId)))
        {
            foreach (var expansionId in data.Manifest.Expansions)
            {
                if (ImGui.Selectable(ExpansionNames.LongName(expansionId), expansionId == config.SelectedExpansionId))
                {
                    config.SelectedExpansionId = expansionId;
                    config.TrackerLineFilter = string.Empty; // focus is per-expansion
                    config.OnSettingChanged();
                }
            }

            ImGui.EndCombo();
        }

        // DoH/DoL has several tool lines per expansion; weapon expansions have one line each.
        List<RelicLine> lines = [.. catalog.LinesFor(config.SelectedExpansionId)];
        var multiLine = lines.Count > 1;
        if (!multiLine)
        {
            if (!string.IsNullOrEmpty(config.TrackerLineFilter))
            {
                config.TrackerLineFilter = string.Empty;
                config.OnSettingChanged();
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(config.TrackerLineFilter) && lines.All(l => l.CollectType != config.TrackerLineFilter))
            {
                config.TrackerLineFilter = string.Empty; // stale from a previous expansion
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Line");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(190);
            var focusLabel = string.IsNullOrEmpty(config.TrackerLineFilter) ? "All lines" : config.TrackerLineFilter;
            if (ImGui.BeginCombo("##line-tracker", focusLabel))
            {
                if (ImGui.Selectable("All lines", string.IsNullOrEmpty(config.TrackerLineFilter)))
                {
                    config.TrackerLineFilter = string.Empty;
                    config.OnSettingChanged();
                }

                foreach (var line in lines)
                {
                    if (ImGui.Selectable(line.CollectType, line.CollectType == config.TrackerLineFilter))
                    {
                        config.TrackerLineFilter = line.CollectType;
                        config.OnSettingChanged();
                    }
                }

                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();

        var hideComplete = config.HideCompleteMaterials;
        if (ImGui.Checkbox("Hide finished materials", ref hideComplete))
        {
            config.HideCompleteMaterials = hideComplete;
            config.OnSettingChanged();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "Filter materials…", ref materialFilter, 128);

        EndStickyHeader();

        DrawShoppingList(config.SelectedExpansionId, ImGui.GetContentRegionAvail().Y);
    }

    private void DrawSettingsTab()
    {
        if (BeginPanel("settings_intro"))
        {
            ImGui.TextColored(MutedColor, "Install Allagan Tools for owned counts (bags, retainers, dresser, armoire — including replicas).");
            ImGui.TextColored(MutedColor, "Relic = per-job steps and notes. Tracker = farm totals. Progress is saved per character.");
            EndPanel();
        }

        DrawAllaganToolsSettingsSection();
        DrawArtisanSettingsSection();

        if (BeginPanel("settings_display"))
        {
            ImGui.TextColored(HeaderColor, "Display");
            ImGui.Spacing();
            var hidePhyseos = config.HidePhyseosRelics;
            if (ImGui.Checkbox("Hide Physeos (Eureka Weapons)", ref hidePhyseos))
            {
                config.HidePhyseosRelics = hidePhyseos;
                config.OnSettingChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Physeos is the Baldesion Arsenal upgrade after Eureka. Same look/stats outside Eureka, "
                    + "and it does not count as a new relic for achievements. When enabled, Eureka counts as "
                    + "finished on Overview, Relic, and Tracker.");
            }

            EndPanel();
        }

        if (BeginPanel("settings_collect"))
        {
            ImGui.TextColored(HeaderColor, "FFXIV Collect (optional)");
            ImGui.SameLine();
            if (config.FfxivCollectCharacterId != 0)
            {
                DrawStatusChip(ffxivCollect.IsLoading ? "Syncing…" : "Linked", ffxivCollect.IsLoading ? StatusChipKind.Warn : StatusChipKind.Ok);
            }
            else
            {
                DrawStatusChip("Off", StatusChipKind.Muted);
            }

            ImGui.TextColored(
                MutedColor,
                "Only needed if you finished relics but no longer have the items in inventory (sold, desynthed, etc.). "
                + "Allagan Tools already covers relics and replicas you still own.");
            ImGui.Spacing();
            DrawCollectSection();
            EndPanel();
        }
    }
}
