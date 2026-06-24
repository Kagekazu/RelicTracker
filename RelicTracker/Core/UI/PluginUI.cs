using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using RelicTracker.Framework;
using RelicTracker.IPC;
using System.Diagnostics;
using System.Numerics;

namespace RelicTracker;

public sealed partial class PluginUI : Window
{
    private const string WindowId = "RelicTracker";

    private static readonly Vector4 HeaderColor = new(0.85f, 0.72f, 0.35f, 1f);
    private static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 GoodColor = new(0.45f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 BadColor = new(0.95f, 0.45f, 0.45f, 1f);

    private readonly Configuration config;
    private readonly RelicDataService data;
    private readonly RelicCatalog catalog;
    private readonly FfxivCollectService ffxivCollect;
    private readonly ItemResolver itemResolver;
    private readonly CollectProgressSync collectSync = new();
    private readonly JobAbbrevResolver jobAbbrevResolver = new();
    private readonly RelicProgressTracker progressTracker;
    private bool drewTitleBarVersion;
    private string materialFilter = string.Empty;

    public PluginUI(Configuration config, RelicDataService data, RelicCatalog catalog, ItemResolver itemResolver, FfxivCollectService ffxivCollect)
        : base($"RelicTracker###{WindowId}")
    {
        this.config = config;
        this.data = data;
        this.catalog = catalog;
        this.itemResolver = itemResolver;
        this.ffxivCollect = ffxivCollect;
        progressTracker = new RelicProgressTracker(config, collectSync);
        jobAbbrevResolver.Build();

        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(720, 560);

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Heart,
            ShowTooltip = () => ImGui.SetTooltip("Ko-fi (because relics are thirsty work)"),
            Click = _ => Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/kagekazu",
                UseShellExecute = true,
            }),
        });
    }

    public override void OnClose()
    {
        config.PersistIfDirty();
        TitleBarVersion.ClearCache();
        base.OnClose();
    }

    public override void PostDraw()
    {
        if (!drewTitleBarVersion)
        {
            TitleBarVersion.DrawFromWindowLookup(
                TitleBarButtons.Count,
                AllowPinning || AllowClickthrough,
                WindowName);
        }

        drewTitleBarVersion = false;
        base.PostDraw();
    }

    public override void Draw()
    {
        TitleBarVersion.DrawFromContext(
            TitleBarButtons.Count,
            AllowPinning || AllowClickthrough,
            WindowName);
        drewTitleBarVersion = true;

        DrawHeader();
        ImGui.Spacing();

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

            if (ImGui.BeginTabItem("Materials ref"))
            {
                DrawMaterialsReferenceTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Collect"))
            {
                DrawCollectTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(HeaderColor, "RelicTracker");
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"({data.Manifest.SheetVersion}, {data.Manifest.Patch})");

        ImGui.Spacing();
        DrawDependencyStatus();
    }

    private void DrawDependencyStatus()
    {
        if (!AllaganToolsIpc.IsInstalled)
        {
            ImGui.TextColored(WarningColor, "Allagan Tools is not installed.");
            return;
        }

        if (!AllaganToolsIpc.IsEnabled)
        {
            ImGui.TextColored(WarningColor, "Allagan Tools is installed but not enabled.");
            return;
        }

        if (!AllaganToolsIpc.IsReady)
        {
            ImGui.TextColored(WarningColor, "Allagan Tools is loading inventory data…");
            return;
        }

        ImGui.TextColored(GoodColor, "Allagan Tools connected");
    }

    private void DrawTrackerTab()
    {
        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));
        progressTracker.EnsureCollectSynced(ffxivCollect, data);

        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("Expansion", config.SelectedExpansionId))
        {
            foreach (var expansionId in data.Manifest.Expansions)
            {
                if (ImGui.Selectable(expansionId, expansionId == config.SelectedExpansionId))
                {
                    config.SelectedExpansionId = expansionId;
                    config.OnSettingChanged();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        DrawTrackerProgressHint();

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
        ImGui.TextColored(MutedColor, "Per-job progress lives on the Relic tab.");

        ImGui.Spacing();

        DrawMaterialsTable(config.SelectedExpansionId, ImGui.GetContentRegionAvail().Y);
    }

    private void DrawTrackerProgressHint()
    {
        if (progressTracker.UsesCollectProgress)
        {
            ImGui.TextColored(GoodColor, "Progress from Collect");
            return;
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextColored(MutedColor, "Set Collect ID for auto progress");
            return;
        }

        if (ffxivCollect.IsLoading)
        {
            ImGui.TextColored(MutedColor, "Loading Collect…");
            return;
        }

        ImGui.TextColored(MutedColor, "Manual progress — counts assume all jobs incomplete");
    }

    private void DrawMaterialsReferenceTab()
    {
        ImGui.TextColored(MutedColor, $"Acquisition reference for {config.SelectedExpansionId} (matches Tracker expansion).");
        ImGui.Spacing();

        var rows = data.GetMaterialReference(config.SelectedExpansionId).ToList();
        if (rows.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No reference entries for this expansion.");
            return;
        }

        using var table = ImRaii.Table("MaterialRef", 4, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new Vector2(0, -1));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Requirement", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Step ?? "");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Material ?? "");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Location ?? "");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Requirement ?? "");
        }
    }

    private void DrawSettingsTab()
    {
        var activeOnly = config.ActiveCharacterOnly;
        if (ImGui.Checkbox("Active character + retainers only", ref activeOnly))
        {
            config.ActiveCharacterOnly = activeOnly;
            config.OnSettingChanged();
        }

        ImGui.TextColored(MutedColor, "When off, counts all characters tracked by Allagan Tools.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("FFXIV Collect character ID is configured on the Collect tab.");
        ImGui.TextColored(MutedColor, "Data is read-only from ffxivcollect.com. Refresh your character there after earning relics.");
    }
}
