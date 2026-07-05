using RelicTracker.IPC;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private static readonly string[] CollectCategories = ["all", "weapons", "tools", "armor", "ultimate"];

    private int collectCategoryIndex;
    private string collectCharacterIdInput = string.Empty;
    private string collectFilter = string.Empty;
    private bool collectInputInitialized;
    private bool collectShowMissing;

    private void DrawCollectSection()
    {
        if (!collectInputInitialized)
        {
            collectCharacterIdInput = config.FfxivCollectCharacterId == 0
                ? string.Empty
                : config.FfxivCollectCharacterId.ToString();
            collectInputInitialized = true;
        }

        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));

        ImGui.TextColored(MutedColor, "Read-only profile sync — use when relics are no longer in your inventory.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##collectCharacterId", "Character ID", ref collectCharacterIdInput, 32);

        ImGui.SameLine();
        if (ImGui.Button("Save ID"))
        {
            if (ulong.TryParse(collectCharacterIdInput.Trim(), out var parsed) && parsed > 0)
            {
                config.FfxivCollectCharacterId = parsed;
                config.OnSettingChanged();
                InvalidateOwnershipCache();
                ffxivCollect.Refresh(parsed);
            }
            else
            {
                config.FfxivCollectCharacterId = 0;
                config.OnSettingChanged();
                InvalidateOwnershipCache();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(config.FfxivCollectCharacterId == 0 && !AllaganToolsIpc.IsReady))
        {
            if (ImGui.Button("Recheck"))
            {
                TriggerProgressRecheck();
            }
        }

        if (config.FfxivCollectCharacterId > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open profile"))
            {
                ShellStart($"https://ffxivcollect.com/characters/{config.FfxivCollectCharacterId}");
            }
        }

        if (ffxivCollect.IsLoading)
        {
            ImGui.TextColored(MutedColor, "Loading…");
        }
        else if (!string.IsNullOrWhiteSpace(ffxivCollect.StatusMessage))
        {
            ImGui.TextColored(WarningColor, ffxivCollect.StatusMessage);
        }
        else if (ffxivCollect.LastRefreshUtc is DateTime refreshed)
        {
            ImGui.TextColored(
                GoodColor,
                $"Owned {ffxivCollect.Snapshot.Owned.Count} · Missing {ffxivCollect.Snapshot.Missing.Count} · Updated {refreshed.ToLocalTime():t}");
        }

        ImGui.Spacing();

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextWrapped(
                "Find your character ID in the URL on ffxivcollect.com when viewing your profile, e.g. ffxivcollect.com/characters/123456");
            return;
        }

        ImGui.SetNextItemWidth(140);
        if (ImGui.BeginCombo("Category", CollectCategories[collectCategoryIndex]))
        {
            for (var i = 0; i < CollectCategories.Length; i++)
            {
                if (ImGui.Selectable(CollectCategories[i], i == collectCategoryIndex))
                {
                    collectCategoryIndex = i;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Show missing", ref collectShowMissing);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##collectFilter", "Filter…", ref collectFilter, 128);

        ImGui.Spacing();
        DrawCollectTable(collectShowMissing ? ffxivCollect.Snapshot.Missing : ffxivCollect.Snapshot.Owned);
    }

    private void DrawCollectTable(List<FfxivCollectRelic> relics)
    {
        var rows = FilterCollectRelics(relics);
        if (rows.Count == 0)
        {
            ImGui.TextColored(MutedColor, collectShowMissing ? "No missing relics in this category." : "No owned relics in this category.");
            return;
        }

        using var table = ImRaii.Table(
            "CollectRelics",
            3,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
            new(0, -1));

        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Relic", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableHeadersRow();

        foreach (var relic in rows.OrderBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextWrapped(relic.TypeName);

            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{relic.Name}###collect_{relic.Id}", false, ImGuiSelectableFlags.SpanAllColumns))
            {
                ShellStart($"https://ffxivcollect.com/relics/{relic.Id}");
            }

            ImGui.TableNextColumn();
            ImGui.Text(relic.Category);
        }
    }

    private List<FfxivCollectRelic> FilterCollectRelics(List<FfxivCollectRelic> relics)
    {
        IEnumerable<FfxivCollectRelic> query = relics;

        var category = CollectCategories[collectCategoryIndex];
        if (category != "all")
        {
            query = query.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(collectFilter))
        {
            query = query.Where(r =>
                r.Name.Contains(collectFilter, StringComparison.OrdinalIgnoreCase) ||
                r.TypeName.Contains(collectFilter, StringComparison.OrdinalIgnoreCase));
        }

        return [.. query];
    }

    private void TriggerProgressRecheck()
    {
        var collectLinked = config.FfxivCollectCharacterId != 0;
        if (!collectLinked && !AllaganToolsIpc.IsReady)
        {
            return;
        }

        InvalidateOwnershipCache();

        if (collectLinked)
        {
            ffxivCollect.ForceRefresh(config.FfxivCollectCharacterId);
        }
    }

    private void DrawProgressRecheckButton()
    {
        if (config.FfxivCollectCharacterId == 0 && !AllaganToolsIpc.IsReady)
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Recheck"))
        {
            TriggerProgressRecheck();
        }

        if (ImGui.IsItemHovered())
        {
            var tooltip = config.FfxivCollectCharacterId != 0 && AllaganToolsIpc.IsReady
                ? "Refresh FFXIV Collect and re-read Allagan Tools inventory counts."
                : config.FfxivCollectCharacterId != 0
                    ? "Fetch the latest relic progress from FFXIV Collect."
                    : "Re-read owned relic items (and replicas) from Allagan Tools inventory.";
            ImGui.SetTooltip(tooltip);
        }
    }
}
