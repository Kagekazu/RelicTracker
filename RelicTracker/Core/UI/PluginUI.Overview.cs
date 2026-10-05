namespace RelicTracker;

public sealed partial class PluginUI
{
    private string overviewFilter = string.Empty;

    private void DrawOverviewTab()
    {
        if (DrawCatalogLoadError())
        {
            return;
        }

        RefreshCollectIfStale();

        var ownership = GetOwnership();
        var statuses = RelicStatusService.Build(ownership, catalog, config.HidePhyseosRelics);

        DrawOverviewStickyHeader(statuses);
        EndStickyHeader();

        using var scroll = ImRaii.Child("##OverviewScroll", new(0, -1), false);
        if (!scroll)
        {
            return;
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            DrawProgressSourceHint(ProgressHintContext.Overview);
        }

        List<(string ExpansionId, List<RelicLineStatus> Lines, List<ArmorLine> ArmorLines)> expansions = [];
        foreach (var expansionId in catalog.Expansions)
        {
            List<RelicLineStatus> lines =
            [
                .. statuses
                    .Where(status => string.Equals(status.Line.Expansion, expansionId, StringComparison.Ordinal))
                    .Where(MatchesOverviewFilter)
            ];

            List<ArmorLine> armorLines = [.. catalog.ArmorLinesFor(expansionId).Where(armor => MatchesArmorFilter(armor, ownership))];
            if (lines.Count > 0 || armorLines.Count > 0)
            {
                expansions.Add((expansionId, lines, armorLines));
            }
        }

        if (!BeginPanel("overview_lines"))
        {
            return;
        }

        if (expansions.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No lines match this filter.");
        }
        else
        {
            DrawOverviewTable(expansions, ownership);
        }

        EndPanel();
    }

    private void DrawOverviewStickyHeader(IReadOnlyList<RelicLineStatus> statuses)
    {
        var summary = RelicStatusService.Summarize(statuses);

        ImGui.TextColored(HeaderColor, "Relic collection");
        ImGui.SameLine();
        if (ffxivCollect.IsLoading)
        {
            DrawStatusChip("Syncing…", StatusChipKind.Warn);
        }
        else if (ffxivCollect.LastRefreshUtc is DateTime refreshed)
        {
            DrawStatusChip($"Updated {refreshed.ToLocalTime():t}", StatusChipKind.Muted);
        }

        DrawProgressRecheckButton();

        ImGui.Text($"{summary.LinesComplete} of {summary.LineCount} relic lines complete");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "A relic line is one relic series (Zodiac, Anima, Lucis tools, ...).\n"
                + "It's complete once every job has its final upgrade. Armor isn't counted here.");
        }

        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"·  {summary.JobsComplete} of {summary.JobsTotal} relics fully upgraded");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Counts each job on each relic line separately (for example 13 Anima weapons, 11 Lucis tools).");
        }

        DrawPercentBar(summary.Percent, Math.Min(320f, ImGui.GetContentRegionAvail().X), $"{summary.Percent * 100f:0}% of all upgrade steps");

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

    /// <summary>One table for every expansion, each a collapsible divider row over its lines.</summary>
    private void DrawOverviewTable(
        IReadOnlyList<(string ExpansionId, List<RelicLineStatus> Lines, List<ArmorLine> ArmorLines)> expansions,
        RelicOwnership ownership)
    {
        // End ImGui tables before EndPanel - ending the child first crashes ImGui.
        using var table = ImRaii.Table(
            "OverviewLines",
            4,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Relic", ImGuiTableColumnFlags.WidthStretch, 0.36f);
        ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("What's left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableHeadersRow();

        foreach ((string expansionId, List<RelicLineStatus> lines, List<ArmorLine> armorLines) in expansions)
        {
            if (!DrawOverviewExpansionRow(expansionId, lines))
            {
                continue;
            }

            foreach (var status in lines)
            {
                DrawOverviewLineRow(status);
            }

            foreach (var armor in armorLines)
            {
                DrawOverviewArmorRow(armor, ownership);
            }
        }
    }

    /// <summary>Expansion divider row; finished expansions start collapsed. Returns true when open.</summary>
    private bool DrawOverviewExpansionRow(string expansionId, IReadOnlyList<RelicLineStatus> lines)
    {
        var jobsComplete = lines.Sum(line => line.JobsComplete);
        var jobsTotal = lines.Sum(line => line.Line.Jobs);
        var allDone = lines.Count > 0 && lines.All(line => line.IsComplete);

        var title = ExpansionNames.LongName(expansionId);
        var header = jobsTotal == 0
            ? title
            : allDone
                ? $"{title} - done"
                : $"{title} - {jobsComplete}/{jobsTotal} maxed";

        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        ImGui.TableNextColumn();
        var flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen
                    | (allDone ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen);
        using var color = ImRaii.PushColor(ImGuiCol.Text, HeaderColor);
        return ImGui.TreeNodeEx($"{header}###overview_{expansionId}", flags);
    }

    private void DrawOverviewArmorRow(ArmorLine armor, RelicOwnership ownership)
    {
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var setsDone = armor.Sets.Count(set => IsSetComplete(set, ownership));
        var fraction = total > 0 ? (float)owned / total : 0f;
        var complete = total > 0 && owned >= total;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{armor.LineName} (armor)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(BuildArmorTooltip(armor, ownership));
        }

        ImGui.TableNextColumn();
        DrawDoneCount(owned, total);

        ImGui.TableNextColumn();
        DrawPercentBar(fraction, 140f, $"{fraction * 100f:0}%");

        ImGui.TableNextColumn();
        if (complete)
        {
            ImGui.TextColored(GoodColor, "All pieces collected");
        }
        else
        {
            ImGui.TextUnformatted($"{setsDone}/{armor.Sets.Count} sets complete");
        }
    }

    /// <summary>"done/total": green when complete, muted when nothing is done yet, plain otherwise.</summary>
    private static void DrawDoneCount(int done, int total)
    {
        var text = $"{done}/{total}";
        if (total > 0 && done >= total)
        {
            ImGui.TextColored(GoodColor, text);
        }
        else if (done == 0)
        {
            ImGui.TextColored(MutedColor, text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }

    private static int OwnedPieces(ArmorLine armor, RelicOwnership ownership) =>
        armor.AllTiers.Sum(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces));

    private static bool IsSetComplete(ArmorSet set, RelicOwnership ownership) =>
        set.Tiers.All(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces) >= tier.Pieces);

    private static string BuildArmorTooltip(ArmorLine armor, RelicOwnership ownership)
    {
        List<string> lines = [$"{armor.LineName} - pieces owned per set:"];
        foreach (var set in armor.Sets)
        {
            lines.Add(string.Empty);
            lines.Add(set.Name + ":");
            foreach (var tier in set.Tiers)
            {
                var tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
                lines.Add($"  {tier.Label}: {tierOwned}/{tier.Pieces}");
            }
        }

        return string.Join("\n", lines);
    }

    private bool MatchesArmorFilter(ArmorLine armor, RelicOwnership ownership)
    {
        if (!string.IsNullOrWhiteSpace(overviewFilter)
            && !armor.LineName.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase)
            && !armor.Expansion.Contains(overviewFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (config.OverviewIncompleteOnly && OwnedPieces(armor, ownership) >= armor.TotalPieces)
        {
            return false;
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
        DrawDoneCount(status.JobsComplete, status.Line.Jobs);

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

    private static string BuildFrontierText(RelicLineStatus status)
    {
        List<(int Count, string Step)> frontiers = [];

        if (status.JobsNotStarted > 0 && status.TierCount > 0)
        {
            frontiers.Add((status.JobsNotStarted, status.Line.StepName(0)));
        }

        for (var tier = 0; tier < status.TierCount - 1; tier++)
        {
            var count = status.JobsAtStep(tier);
            if (count > 0)
            {
                frontiers.Add((count, status.Line.StepName(tier + 1)));
            }
        }

        if (frontiers.Count == 0)
        {
            return "-";
        }

        List<string> parts = [.. frontiers.Select(frontier => $"{frontier.Count} on {frontier.Step}")];
        return parts.Count <= 3
            ? string.Join(",  ", parts)
            : string.Join(",  ", parts.Take(3)) + $",  +{parts.Count - 3} more";
    }

    private static string BuildLineTooltip(RelicLineStatus status)
    {
        List<string> lines =
        [
            status.Line.CollectType,
            $"{status.JobsComplete}/{status.Line.Jobs} jobs fully complete",
            string.Empty,
            "Jobs that reached each step:"
        ];

        for (var tier = 0; tier < status.TierCount; tier++)
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
}
