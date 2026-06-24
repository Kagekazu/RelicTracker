using System.Numerics;
using RelicTracker.Framework;
using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private void DrawMaterialsTable(string expansionId, float regionHeight)
    {
        using var pane = ImRaii.Child("##TrackerMaterialsPane", new Vector2(0, regionHeight), false);
        if (!pane)
        {
            return;
        }

        var rows = data.GetExpansionMaterials(
            expansionId,
            itemResolver,
            itemId => AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly),
            progressTracker).ToList();

        if (rows.Count == 0)
        {
            if (data.Expansions.Count == 0)
            {
                ImGui.TextColored(WarningColor, "Relic data failed to load. Rebuild the plugin or check /xllog for JSON errors.");
            }
            else
            {
                ImGui.TextColored(MutedColor, "No materials for this expansion.");
            }

            return;
        }

        var hideComplete = config.HideCompleteMaterials;
        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            rows = rows
                .Where(r => r.Name.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                            || (r.Step?.Contains(materialFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (r.Label?.Contains(materialFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                            || r.Section.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                            || (r.JobsNeeded?.Contains(materialFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (hideComplete)
        {
            rows = rows.Where(r => r.Shortfall > 0).ToList();
        }

        DrawMaterialsSummary(rows, hideComplete);

        ImGui.Spacing();

        var sectionOrder = CollectStepMap.GetSectionOrder(expansionId);
        var rowsBySection = rows
            .GroupBy(row => row.Section, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var drewAnySection = false;
        foreach (var section in sectionOrder)
        {
            if (!rowsBySection.TryGetValue(section, out var sectionRows) || sectionRows.Count == 0)
            {
                continue;
            }

            drewAnySection = true;
            DrawMaterialSection(expansionId, section, sectionRows);
        }

        foreach (var (section, sectionRows) in rowsBySection.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (sectionOrder.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            drewAnySection = true;
            DrawMaterialSection(expansionId, section, sectionRows);
        }

        if (!drewAnySection)
        {
            ImGui.TextColored(MutedColor, "No materials match the current filter.");
        }
    }

    private void DrawMaterialsSummary(IReadOnlyList<MaterialDisplayRow> rows, bool hideComplete)
    {
        var shortfallRows = rows.Count(r => !r.IsCurrency && r.Shortfall > 0);
        var unresolvedRows = rows.Count(r => !r.IsCurrency && !r.IsResolved);
        if (shortfallRows == 0 && unresolvedRows == 0)
        {
            ImGui.TextColored(GoodColor, hideComplete
                ? "Nothing left to farm for this expansion."
                : "You have enough of every tracked material for this expansion.");
            return;
        }

        if (shortfallRows > 0)
        {
            ImGui.TextColored(BadColor, $"{shortfallRows} material{(shortfallRows == 1 ? string.Empty : "s")} still needed.");
            if (unresolvedRows > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(WarningColor, $"({unresolvedRows} not linked to an item)");
            }

            return;
        }

        if (unresolvedRows > 0)
        {
            ImGui.TextColored(WarningColor, $"{unresolvedRows} material{(unresolvedRows == 1 ? string.Empty : "s")} could not be matched to an item ID.");
        }
    }

    private void DrawMaterialSection(string expansionId, string section, IReadOnlyList<MaterialDisplayRow> rows)
    {
        var shortfall = rows.Count(row => !row.IsCurrency && row.Shortfall > 0);
        var header = shortfall > 0 ? $"{section} ({shortfall} needed)" : section;
        var configKey = $"{expansionId}|{section}";
        var isOpen = config.ExpandedMaterialSections.TryGetValue(configKey, out var savedOpen)
            ? savedOpen
            : shortfall > 0;

        var nodeOpen = ImGui.CollapsingHeader(
            header,
            isOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (nodeOpen != isOpen)
        {
            config.ExpandedMaterialSections[configKey] = nodeOpen;
            config.OnSettingChanged();
        }
        else if (!config.ExpandedMaterialSections.ContainsKey(configKey))
        {
            config.ExpandedMaterialSections[configKey] = nodeOpen;
        }

        if (!nodeOpen)
        {
            return;
        }

        ImGui.Spacing();
        DrawMaterialRowsTable(section, rows);
        ImGui.Spacing();
    }

    private void DrawMaterialRowsTable(string section, IReadOnlyList<MaterialDisplayRow> rows)
    {
        using var table = ImRaii.Table(
            $"RelicMaterials_{section}",
            5,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.ScrollY,
            new Vector2(0, Math.Min(280f, (rows.Count + 1) * ImGui.GetTextLineHeightWithSpacing() + 8f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 88);
        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var stepLabel = row.DisplayStep;
            ImGui.TextUnformatted(stepLabel);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(stepLabel);
            }

            ImGui.TableNextColumn();
            if (row.IsCurrency)
            {
                ImGui.TextColored(MutedColor, row.Name);
            }
            else if (!row.IsResolved)
            {
                ImGui.TextColored(WarningColor, row.Name);
            }
            else
            {
                ImGui.Text(row.Name);
                if (row.ItemIds.Count > 1 && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Counts inventory across {row.ItemIds.Count} related items.");
                }
            }

            ImGui.TableNextColumn();
            ImGui.Text(row.Needed.ToString());

            ImGui.TableNextColumn();
            if (row.IsCurrency && !row.IsCurrencyTracked)
            {
                ImGui.TextColored(MutedColor, "—");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Wallet currency tracking is not available for this entry.");
                }
            }
            else
            {
                ImGui.Text(row.Owned.ToString());
            }

            ImGui.TableNextColumn();
            if (row.IsCurrency && !row.IsCurrencyTracked)
            {
                ImGui.TextColored(MutedColor, "—");
            }
            else
            {
                var color = row.Shortfall == 0 ? GoodColor : BadColor;
                ImGui.TextColored(color, row.Shortfall.ToString());
            }
        }
    }
}
