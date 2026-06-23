using System.Numerics;
using RelicTracker.Framework;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private bool DrawProgressSection(string expansionId)
    {
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            return false;
        }

        var progressRows = RelicProgressTracker.GetProgressRows(sheet);
        if (progressRows.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No per-job progress rows for this expansion.");
            return false;
        }

        var jobNames = RelicProgressTracker.GetJobNames(sheet, jobAbbrevResolver, data.JobColumnsByExpansion);
        var activeJobColumns = RelicProgressTracker.GetActiveJobColumnCount(sheet, progressRows, data.JobColumnsByExpansion);
        var collectMode = progressTracker.UsesCollectProgress;

        var progressOpen = ImGui.CollapsingHeader(
            "Job progress (detail)",
            config.ShowJobProgressSection ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (progressOpen != config.ShowJobProgressSection)
        {
            config.ShowJobProgressSection = progressOpen;
            config.OnSettingChanged();
        }

        if (!progressOpen)
        {
            return false;
        }

        DrawProgressModeHint(collectMode);

        if (!collectMode)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("All done##progress"))
            {
                progressTracker.MarkExpansionComplete(expansionId, sheet);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Reset##progress"))
            {
                progressTracker.ClearExpansion(expansionId);
            }
        }

        ImGui.Spacing();

        var sectionOrder = CollectStepMap.GetSectionOrder(expansionId);
        var rowsBySection = progressRows
            .GroupBy(row => CollectStepMap.ResolveSection(expansionId, row.Step, isCurrency: false), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var drewAnySection = false;
        foreach (var section in sectionOrder)
        {
            if (string.Equals(section, "Currencies", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rowsBySection.TryGetValue(section, out var sectionRows) || sectionRows.Count == 0)
            {
                continue;
            }

            drewAnySection = true;
            DrawProgressSubsection(expansionId, section, sectionRows, jobNames, activeJobColumns, collectMode);
        }

        foreach (var (section, sectionRows) in rowsBySection.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(section, "Currencies", StringComparison.OrdinalIgnoreCase)
                || sectionOrder.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            drewAnySection = true;
            DrawProgressSubsection(expansionId, section, sectionRows, jobNames, activeJobColumns, collectMode);
        }

        if (!drewAnySection)
        {
            ImGui.TextColored(MutedColor, "No job progress rows for this expansion.");
        }

        return true;
    }

    private void DrawProgressSubsection(
        string expansionId,
        string section,
        IReadOnlyList<ProgressRowDefinition> rows,
        IReadOnlyList<string> jobNames,
        int activeJobColumns,
        bool collectMode)
    {
        var incomplete = CountIncompleteProgressCells(expansionId, rows, activeJobColumns);
        var header = incomplete > 0 ? $"{section} ({incomplete} incomplete)" : section;
        var configKey = $"{expansionId}|{section}";
        var isOpen = config.ExpandedMaterialSections.TryGetValue(configKey, out var savedOpen)
            ? savedOpen
            : incomplete > 0;

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
        DrawProgressRowsTable(expansionId, section, rows, jobNames, activeJobColumns, collectMode);
        ImGui.Spacing();
    }

    private int CountIncompleteProgressCells(
        string expansionId,
        IReadOnlyList<ProgressRowDefinition> rows,
        int activeJobColumns)
    {
        var incomplete = 0;
        foreach (var row in rows)
        {
            for (var jobIndex = 0; jobIndex < activeJobColumns && jobIndex < row.Jobs.Count; jobIndex++)
            {
                if (!RelicProgressTracker.IsApplicable(row.Jobs[jobIndex]))
                {
                    continue;
                }

                if (!progressTracker.IsComplete(expansionId, row.Step, row.Label, jobIndex, row.Jobs))
                {
                    incomplete++;
                }
            }
        }

        return incomplete;
    }

    private void DrawProgressRowsTable(
        string expansionId,
        string section,
        IReadOnlyList<ProgressRowDefinition> rows,
        IReadOnlyList<string> jobNames,
        int activeJobColumns,
        bool collectMode)
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var tableHeight = Math.Min(
            Math.Max(40f, ImGui.GetContentRegionAvail().Y - 4f),
            (rows.Count + 1) * lineHeight + 8f);

        using var table = ImRaii.Table(
            $"RelicProgress_{section}",
            2 + activeJobColumns,
            ImGuiTableFlags.Resizable
                | ImGuiTableFlags.BordersInnerV
                | ImGuiTableFlags.BordersOuterH
                | ImGuiTableFlags.ScrollX
                | ImGuiTableFlags.ScrollY,
            new Vector2(0, tableHeight));

        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 88);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 112);
        for (var jobIndex = 0; jobIndex < activeJobColumns; jobIndex++)
        {
            ImGui.TableSetupColumn(jobNames[jobIndex], ImGuiTableColumnFlags.WidthFixed, 36);
        }

        ImGui.TableSetupScrollFreeze(2, 1);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawProgressTableTextCell(row.Step);

            ImGui.TableNextColumn();
            DrawProgressTableTextCell(string.IsNullOrEmpty(row.Label) ? "—" : row.Label);

            for (var jobIndex = 0; jobIndex < activeJobColumns; jobIndex++)
            {
                ImGui.TableNextColumn();
                if (collectMode)
                {
                    DrawCollectProgressCell(expansionId, row, jobIndex, jobNames);
                }
                else
                {
                    DrawManualProgressCheckbox(expansionId, row, jobIndex, jobNames);
                }
            }
        }
    }

    private static void DrawProgressTableTextCell(string text)
    {
        ImGui.TextUnformatted(text);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    private void DrawProgressModeHint(bool collectMode)
    {
        if (collectMode)
        {
            ImGui.TextColored(GoodColor, "Auto from FFXIV Collect owned relics.");
            return;
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextColored(MutedColor, "Manual progress — set a Collect character ID for auto sync.");
            return;
        }

        if (ffxivCollect.IsLoading)
        {
            ImGui.TextColored(MutedColor, "Loading Collect… using manual progress until sync completes.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ffxivCollect.StatusMessage))
        {
            ImGui.TextColored(WarningColor, $"Collect unavailable ({ffxivCollect.StatusMessage}). Manual progress.");
            return;
        }

        ImGui.TextColored(WarningColor, "Refresh Collect on the Collect tab to enable auto sync. Manual progress for now.");
    }

    private void DrawCollectProgressCell(
        string expansionId,
        ProgressRowDefinition row,
        int jobIndex,
        IReadOnlyList<string> jobNames)
    {
        if (jobIndex >= row.Jobs.Count || !RelicProgressTracker.IsApplicable(row.Jobs[jobIndex]))
        {
            return;
        }

        var complete = progressTracker.IsComplete(expansionId, row.Step, row.Label, jobIndex, row.Jobs);
        ImGui.TextColored(complete ? GoodColor : MutedColor, complete ? "✓" : "—");

        if (ImGui.IsItemHovered())
        {
            var jobName = RelicProgressTracker.ResolveJobDisplayName(jobNames, jobIndex, jobAbbrevResolver);
            ImGui.SetTooltip($"{jobName}\n{row.Step} — {row.Label}\n{(complete ? "Complete" : "Incomplete")} (FFXIV Collect)");
        }
    }

    private void DrawManualProgressCheckbox(
        string expansionId,
        ProgressRowDefinition row,
        int jobIndex,
        IReadOnlyList<string> jobNames)
    {
        if (jobIndex >= row.Jobs.Count)
        {
            return;
        }

        if (!RelicProgressTracker.IsApplicable(row.Jobs[jobIndex]))
        {
            return;
        }

        var complete = progressTracker.IsComplete(expansionId, row.Step, row.Label, jobIndex, row.Jobs);
        ImGui.PushID($"{row.Step}|{row.Label}|{jobIndex}");
        if (ImGui.Checkbox("##done", ref complete))
        {
            progressTracker.SetComplete(expansionId, row.Step, row.Label, jobIndex, complete);
        }

        if (ImGui.IsItemHovered())
        {
            var jobName = RelicProgressTracker.ResolveJobDisplayName(jobNames, jobIndex, jobAbbrevResolver);
            ImGui.SetTooltip($"{jobName}\n{row.Step} — {row.Label}");
        }

        ImGui.PopID();
    }
}
