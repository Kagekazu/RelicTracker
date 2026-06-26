using System.Numerics;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private static readonly string[] ExpansionLongNames =
    [
        "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers",
        "Endwalker", "Dawntrail", "Crafters & Gatherers"
    ];

    private string overviewFilter = string.Empty;

    private void DrawOverviewTab()
    {
        if (!catalog.IsLoaded || catalog.Lines.Count == 0)
        {
            ImGui.TextColored(WarningColor, "Relic catalog failed to load. Rebuild the plugin or check /xllog.");
            return;
        }

        if (config.FfxivCollectCharacterId != 0)
        {
            ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));
        }
        else
        {
            ImGui.TextColored(MutedColor,
                "Standalone mode — tick steps on the Relic tab to fill this in. Link FFXIV Collect on the Settings tab to auto-fill finished relics.");
            ImGui.Spacing();
        }

        RelicOwnership ownership = GetOwnership();
        IReadOnlyList<RelicLineStatus> statuses = RelicStatusService.Build(ownership, catalog);
        DrawOverviewHeader(statuses);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using ImRaii.ChildDisposable scroll = ImRaii.Child("##OverviewScroll", new(0, -1), false);
        if (!scroll)
        {
            return;
        }

        foreach (string expansionId in catalog.Expansions)
        {
            List<RelicLineStatus> lines = [.. statuses
                .Where(status => string.Equals(status.Line.Expansion, expansionId, StringComparison.Ordinal))
                .Where(MatchesOverviewFilter)];

            List<ArmorLine> armorLines = [.. catalog.ArmorLinesFor(expansionId).Where(armor => MatchesArmorFilter(armor, ownership))];

            if (lines.Count == 0 && armorLines.Count == 0)
            {
                continue;
            }

            DrawOverviewExpansion(expansionId, lines, armorLines, ownership);
        }
    }

    private void DrawOverviewHeader(IReadOnlyList<RelicLineStatus> statuses)
    {
        RelicProgressSummary summary = RelicStatusService.Summarize(statuses);

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
        bool incompleteOnly = config.OverviewIncompleteOnly;
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
        int jobsComplete = lines.Sum(line => line.JobsComplete);
        int jobsTotal = lines.Sum(line => line.Line.Jobs);
        bool allDone = lines.Count > 0 && lines.All(line => line.IsComplete);

        string title = $"{ExpansionLongName(expansionId)} ({expansionId})";
        string header = jobsTotal == 0
            ? title
            : allDone
                ? $"{title} — done"
                : $"{title} — {jobsComplete}/{jobsTotal} maxed";

        // Finished expansions collapse by default; ones you're still working on stay open.
        ImGuiTreeNodeFlags headerFlags = allDone ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen;
        if (!ImGui.CollapsingHeader($"{header}###overview_{expansionId}", headerFlags))
        {
            return;
        }

        using ImRaii.TableDisposable table = ImRaii.Table(
            $"OverviewLines_{expansionId}",
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

        foreach (RelicLineStatus status in lines)
        {
            DrawOverviewLineRow(status);
        }

        foreach (ArmorLine armor in armorLines)
        {
            DrawOverviewArmorRow(armor, ownership);
        }
    }

    private void DrawOverviewArmorRow(ArmorLine armor, RelicOwnership ownership)
    {
        int owned = OwnedPieces(armor, ownership);
        int total = armor.TotalPieces;
        int setsDone = armor.Sets.Count(set => IsSetComplete(set, ownership));
        float fraction = total > 0 ? (float)owned / total : 0f;
        bool complete = total > 0 && owned >= total;

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{armor.LineName} (armor)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(BuildArmorTooltip(armor, ownership));
        }

        ImGui.TableNextColumn();
        Vector4 doneColor = complete ? GoodColor : owned > 0 ? WarningColor : MutedColor;
        ImGui.TextColored(doneColor, $"{owned}/{total}");

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

    private static int OwnedPieces(ArmorLine armor, RelicOwnership ownership) =>
        armor.AllTiers.Sum(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces));

    private static bool IsSetComplete(ArmorSet set, RelicOwnership ownership) =>
        set.Tiers.All(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces) >= tier.Pieces);

    private static string BuildArmorTooltip(ArmorLine armor, RelicOwnership ownership)
    {
        List<string> lines = [$"{armor.LineName} — pieces owned per set:"];
        foreach (ArmorSet set in armor.Sets)
        {
            lines.Add(string.Empty);
            lines.Add(set.Name + ":");
            foreach (ArmorTier tier in set.Tiers)
            {
                int tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
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
        Vector4 doneColor = status.IsComplete ? GoodColor : status.JobsComplete > 0 ? WarningColor : MutedColor;
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
        Vector4 color = fraction >= 1f ? GoodColor : fraction > 0f ? WarningColor : MutedColor;
        using ImRaii.ColorDisposable barColor = ImRaii.PushColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(Math.Clamp(fraction, 0f, 1f), new(width, ImGui.GetFrameHeight()), overlay);
    }

    /// <summary>Concise "what step are you on" summary: how many jobs need each upcoming step next.</summary>
    private static string BuildFrontierText(RelicLineStatus status)
    {
        List<(int Count, string Step)> frontiers = [];

        if (status.JobsNotStarted > 0 && status.Line.TierCount > 0)
        {
            frontiers.Add((status.JobsNotStarted, status.Line.StepName(0)));
        }

        for (int tier = 0; tier < status.Line.TierCount - 1; tier++)
        {
            int count = status.JobsAtStep(tier);
            if (count > 0)
            {
                frontiers.Add((count, status.Line.StepName(tier + 1)));
            }
        }

        if (frontiers.Count == 0)
        {
            return "—";
        }

        List<string> parts = [.. frontiers.Select(frontier => $"{frontier.Count} → {frontier.Step}")];
        return parts.Count <= 3
            ? string.Join(",  ", parts)
            : string.Join(",  ", parts.Take(3)) + $",  +{parts.Count - 3} more";
    }

    private static string BuildLineTooltip(RelicLineStatus status)
    {
        List<string> lines =
        [
            $"{status.Line.CollectType} ({status.Line.Category})",
            $"{status.JobsComplete}/{status.Line.Jobs} jobs fully complete",
            string.Empty,
            "Jobs that reached each step:"
        ];

        for (int tier = 0; tier < status.Line.TierCount; tier++)
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
            var _ => expansionId
        };
    }
}
