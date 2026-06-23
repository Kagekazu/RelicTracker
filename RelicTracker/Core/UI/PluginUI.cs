using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using RelicTracker.Framework;
using RelicTracker.IPC;
using System.Diagnostics;
using System.Numerics;

namespace RelicTracker;

public sealed class PluginUI : Window
{
    private const string WindowId = "RelicTracker";

    private static readonly Vector4 HeaderColor = new(0.85f, 0.72f, 0.35f, 1f);
    private static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 GoodColor = new(0.45f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 BadColor = new(0.95f, 0.45f, 0.45f, 1f);

    private readonly Configuration config;
    private readonly RelicDataService data;
    private readonly ItemResolver itemResolver;
    private bool drewTitleBarVersion;
    private bool hideComplete;
    private string materialFilter = string.Empty;

    public PluginUI(Configuration config, RelicDataService data, ItemResolver itemResolver)
        : base($"RelicTracker###{WindowId}")
    {
        this.config = config;
        this.data = data;
        this.itemResolver = itemResolver;

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
        if (!AllaganToolsIpc.IsPluginLoaded)
        {
            ImGui.TextColored(WarningColor, "Allagan Tools is not loaded. Install it for inventory counts.");
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
        ImGui.TextColored(MutedColor, "Per-weapon/tool quantities from Wyn's tracker (v1). Progress checkboxes coming later.");
        ImGui.Spacing();

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
        ImGui.Checkbox("Hide complete", ref hideComplete);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "Filter materials…", ref materialFilter, 128);

        ImGui.Spacing();
        DrawMaterialsTable(config.SelectedExpansionId);
    }

    private void DrawMaterialsTable(string expansionId)
    {
        var rows = data.GetExpansionMaterials(
            expansionId,
            itemResolver,
            itemId => AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly)).ToList();

        if (rows.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No materials for this expansion.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            rows = rows
                .Where(r => r.Name.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                            || (r.Step?.Contains(materialFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (hideComplete)
        {
            rows = rows.Where(r => r.Shortfall > 0 || r.IsCurrency).ToList();
        }

        using var table = ImRaii.Table("RelicMaterials", 5, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY, new Vector2(0, -1));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.Step ?? "—");

            ImGui.TableNextColumn();
            if (row.IsCurrency)
            {
                ImGui.TextColored(MutedColor, row.Name);
            }
            else if (row.ItemId is null)
            {
                ImGui.TextColored(WarningColor, row.Name);
            }
            else
            {
                ImGui.Text(row.Name);
            }

            ImGui.TableNextColumn();
            ImGui.Text(row.Needed.ToString());

            ImGui.TableNextColumn();
            if (row.IsCurrency)
            {
                ImGui.TextColored(MutedColor, "—");
            }
            else
            {
                ImGui.Text(row.Owned.ToString());
            }

            ImGui.TableNextColumn();
            if (row.IsCurrency)
            {
                ImGui.TextColored(MutedColor, "curr");
            }
            else
            {
                var color = row.Shortfall == 0 ? GoodColor : BadColor;
                ImGui.TextColored(color, row.Shortfall.ToString());
            }
        }
    }

    private void DrawMaterialsReferenceTab()
    {
        ImGui.TextColored(MutedColor, "Acquisition reference from Wyn's Materials sheet.");
        ImGui.Spacing();

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

        foreach (var row in data.MaterialReference)
        {
            if (string.IsNullOrWhiteSpace(row.Material) && string.IsNullOrWhiteSpace(row.Location))
            {
                continue;
            }

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

        ImGui.Text("FFXIV Collect character ID (optional, future sync)");
        var collectId = config.FfxivCollectCharacterId.ToString();
        if (ImGui.InputText("##collectId", ref collectId, 32))
        {
            if (ulong.TryParse(collectId, out var parsed))
            {
                config.FfxivCollectCharacterId = parsed;
                config.OnSettingChanged();
            }
        }

        ImGui.TextColored(MutedColor, "Read-only Collect integration is planned; Lodestone refresh remains the official sync path.");
    }
}
