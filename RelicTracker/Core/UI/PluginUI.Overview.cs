using System.Numerics;
using RelicTracker.Framework;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private static readonly string[] ExpansionLongNames =
    [
        "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers",
        "Endwalker", "Dawntrail", "Crafters & Gatherers",
    ];

    private string overviewFilter = string.Empty;

    private void DrawOverviewTab()
    {
        if (!catalog.IsLoaded || catalog.Lines.Count == 0)
        {
            ImGui.TextColored(WarningColor, "Relic catalog failed to load. Rebuild the plugin or check /xllog.");
            return;
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextWrapped(
                "The Overview reads your finished relics from FFXIV Collect. Open the Collect tab and enter your character ID to light it up.");
            return;
        }

        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));

        var statuses = RelicStatusService.Build(ffxivCollect.Snapshot, catalog);
        DrawOverviewHeader(statuses);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using var scroll = ImRaii.Child("##OverviewScroll", new Vector2(0, -1), false);
        if (!scroll)
        {
            return;
        }

        var ownership = GetOwnership();
        foreach (var expansionId in catalog.Expansions)
        {
            var lines = statuses
                .Where(status => string.Equals(status.Line.Expansion, expansionId, StringComparison.Ordinal))
                .Where(MatchesOverviewFilter)
                .ToList();

            var armorLines = catalog.ArmorLinesFor(expansionId)
                .Where(armor => MatchesArmorFilter(armor, ownership))
                .ToList();

            if (lines.Count == 0 && armorLines.Count == 0)
            {
                continue;
            }

            DrawOverviewExpansion(expansionId, lines, armorLines, ownership);
        }
    }

    private void DrawOverviewHeader(IReadOnlyList<RelicLineStatus> statuses)
    {
        var summary = RelicStatusService.Summarize(statuses);

        ImGui.TextColored(HeaderColor, "Relic collection");
        ImGui.SameLine();
        if (ffxivCollect.IsLoading)
        {
            ImGui.TextColored(MutedColor, "(syncing…)");
        }
        else if (ffxivCollect.LastRefreshUtc is DateTime refreshed)
        {
            ImGui.TextColored(MutedColor, $"(updated {refreshed.ToLocalTime():t})");
        }

        ImGui.Text($"{summary.LinesComplete}/{summary.LineCount} relic lines finished on every job");
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"·  {summary.JobsComplete}/{summary.JobsTotal} job relics maxed");

        DrawPercentBar(summary.Percent, 260f, $"{summary.Percent * 100f:0}% of all upgrade steps");

        ImGui.Spacing();
        var incompleteOnly = config.OverviewIncompleteOnly;
        if (ImGui.Checkbox("Hide finished lines", ref incompleteOnly))
        {
            config.OverviewIncompleteOnly = incompleteOnly;
            config.OnSettingChanged();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##overviewFilter", "Filter relics…", ref overviewFilter, 128);
    }

    private void DrawOverviewExpansion(
        string expansionId,
        IReadOnlyList<RelicLineStatus> lines,
        IReadOnlyList<ArmorLine> armorLines,
        RelicOwnership ownership)
    {
        var jobsComplete = lines.Sum(line => line.JobsComplete);
        var jobsTotal = lines.Sum(line => line.Line.Jobs);
        var allDone = lines.Count > 0 && lines.All(line => line.IsComplete);

        var title = $"{ExpansionLongName(expansionId)} ({expansionId})";
        var header = jobsTotal == 0
            ? title
            : allDone ? $"{title} — done" : $"{title} — {jobsComplete}/{jobsTotal} maxed";

        if (!ImGui.CollapsingHeader($"{header}###overview_{expansionId}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (lines.Count > 0)
        {
            using var table = ImRaii.Table(
                $"OverviewLines_{expansionId}",
                4,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
                new Vector2(0, 0));
            if (table)
            {
                ImGui.TableSetupColumn("Relic", ImGuiTableColumnFlags.WidthStretch, 0.36f);
                ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 56);
                ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("What's left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                ImGui.TableHeadersRow();

                foreach (var status in lines)
                {
                    DrawOverviewLineRow(status);
                }
            }
        }

        foreach (var armor in armorLines)
        {
            DrawOverviewArmor(expansionId, armor, ownership);
        }
    }

    private void DrawOverviewArmor(string expansionId, ArmorLine armor, RelicOwnership ownership)
    {
        var owned = armor.Stages.Sum(stage => Math.Min(stage.Pieces, ownership.OwnedCount(stage.CollectType)));
        var total = armor.TotalPieces;
        var fraction = total > 0 ? (float)owned / total : 0f;

        ImGui.Spacing();
        ImGui.TextColored(fraction >= 1f ? GoodColor : HeaderColor, $"{armor.LineName} (armor)");
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"{owned}/{total} pieces");
        ImGui.SameLine();
        DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");

        using var table = ImRaii.Table(
            $"OverviewArmor_{expansionId}_{armor.LineName}",
            3,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Stage", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableHeadersRow();

        foreach (var stage in armor.Stages)
        {
            var stageOwned = Math.Min(stage.Pieces, ownership.OwnedCount(stage.CollectType));
            var stageFraction = stage.Pieces > 0 ? (float)stageOwned / stage.Pieces : 0f;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(stage.Label);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(stage.CollectType);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(stageFraction >= 1f ? GoodColor : MutedColor, $"{stageOwned}/{stage.Pieces}");

            ImGui.TableNextColumn();
            DrawPercentBar(stageFraction, 140f, $"{stageFraction * 100f:0}%");
        }
    }

    private bool MatchesArmorFilter(ArmorLine armor, RelicOwnership ownership)
    {
        if (!string.IsNullOrWhiteSpace(overviewFilter)
            && !armor.LineName.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase)
            && !armor.Expansion.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (config.OverviewIncompleteOnly)
        {
            var owned = armor.Stages.Sum(stage => Math.Min(stage.Pieces, ownership.OwnedCount(stage.CollectType)));
            if (owned >= armor.TotalPieces)
            {
                return false;
            }
        }

        return true;
    }

    private void DrawOverviewLineRow(RelicLineStatus status)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(status.Line.CollectType);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(BuildLineTooltip(status));
        }

        ImGui.TableNextColumn();
        var doneColor = status.IsComplete ? GoodColor : status.JobsComplete > 0 ? WarningColor : MutedColor;
        ImGui.TextColored(doneColor, $"{status.JobsComplete}/{status.Line.Jobs}");

        ImGui.TableNextColumn();
        DrawPercentBar(status.Percent, 140f, $"{status.Percent * 100f:0}%");

        ImGui.TableNextColumn();
        if (status.IsComplete)
        {
            ImGui.TextColored(GoodColor, "All jobs complete");
        }
        else
        {
            ImGui.TextUnformatted(BuildFrontierText(status));
        }
    }

    private void DrawPercentBar(float fraction, float width, string overlay)
    {
        var color = fraction >= 1f ? GoodColor : fraction > 0f ? WarningColor : MutedColor;
        using var barColor = ImRaii.PushColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new Vector2(width, ImGui.GetFrameHeight()), overlay);
    }

    /// <summary>Concise "what step are you on" summary: how many jobs need each upcoming step next.</summary>
    private static string BuildFrontierText(RelicLineStatus status)
    {
        var frontiers = new List<(int Count, string Step)>();

        if (status.JobsNotStarted > 0 && status.Line.TierCount > 0)
        {
            frontiers.Add((status.JobsNotStarted, status.Line.StepName(0)));
        }

        for (var tier = 0; tier < status.Line.TierCount - 1; tier++)
        {
            var count = status.JobsAtStep(tier);
            if (count > 0)
            {
                frontiers.Add((count, status.Line.StepName(tier + 1)));
            }
        }

        if (frontiers.Count == 0)
        {
            return "—";
        }

        var parts = frontiers.Select(frontier => $"{frontier.Count} → {frontier.Step}").ToList();
        return parts.Count <= 3
            ? string.Join(",  ", parts)
            : string.Join(",  ", parts.Take(3)) + $",  +{parts.Count - 3} more";
    }

    private static string BuildLineTooltip(RelicLineStatus status)
    {
        var lines = new List<string>
        {
            $"{status.Line.CollectType} ({status.Line.Category})",
            $"{status.JobsComplete}/{status.Line.Jobs} jobs fully complete",
            string.Empty,
            "Jobs that reached each step:",
        };

        for (var tier = 0; tier < status.Line.TierCount; tier++)
        {
            lines.Add($"  {status.Line.StepName(tier)}: {status.ReachedPerStep[tier]}/{status.Line.Jobs}");
        }

        return string.Join("\n", lines);
    }

    private bool MatchesOverviewFilter(RelicLineStatus status)
    {
        if (config.OverviewIncompleteOnly && status.IsComplete)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(overviewFilter)
            && !status.Line.CollectType.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase)
            && !status.Line.Expansion.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ExpansionLongName(string expansionId)
    {
        return expansionId switch
        {
            "ARR" => ExpansionLongNames[0],
            "HW" => ExpansionLongNames[1],
            "SB" => ExpansionLongNames[2],
            "ShB" => ExpansionLongNames[3],
            "EW" => ExpansionLongNames[4],
            "DT" => ExpansionLongNames[5],
            "DoHDoL" => ExpansionLongNames[6],
            _ => expansionId,
        };
    }
}
