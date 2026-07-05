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

    public PluginUI(Configuration config, RelicDataService data, RelicCatalog catalog, FfxivCollectService ffxivCollect)
        : base($"Relic Tracker###{WindowId}")
    {
        this.config = config;
        this.data = data;
        this.catalog = catalog;
        this.ffxivCollect = ffxivCollect;

        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(720, 560);

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
        if (ImGui.BeginTabBar("RelicTrackerTabs"))
        {
            if (ImGui.BeginTabItem("Overview"))
            {
                DrawOverviewTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Relic"))
            {
                DrawRelicTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Tracker"))
            {
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
        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));

        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("Expansion", config.SelectedExpansionId))
        {
            foreach (var expansionId in data.Manifest.Expansions)
            {
                if (ImGui.Selectable(expansionId, expansionId == config.SelectedExpansionId))
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
            ImGui.SetNextItemWidth(190);
            var focusLabel = string.IsNullOrEmpty(config.TrackerLineFilter) ? "All lines" : config.TrackerLineFilter;
            if (ImGui.BeginCombo("Line", focusLabel))
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
        if (ImGui.Checkbox("Still needed only", ref hideComplete))
        {
            config.HideCompleteMaterials = hideComplete;
            config.OnSettingChanged();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "Filter materials…", ref materialFilter, 128);

        ImGui.SameLine();
        ImGui.TextColored(MutedColor, "Per-job detail is on the Relic tab.");

        ImGui.Spacing();

        DrawShoppingList(config.SelectedExpansionId, ImGui.GetContentRegionAvail().Y);
    }

    private void DrawSettingsTab()
    {
        ImGui.TextColored(MutedColor,
            "Progress is saved per logged-in character. Allagan Tools counts use your active character, retainers, glamour dresser, and armoire.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawAllaganToolsSettingsSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawArtisanSettingsSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("FFXIV Collect (optional)###collectSettings"))
        {
            ImGui.TextColored(
                MutedColor,
                "Only needed if you finished relics but no longer have the items in inventory (sold, desynthed, etc.). "
                + "Allagan Tools already covers relics and replicas you still own.");
            ImGui.Spacing();
            DrawCollectSection();
        }
    }
}
