using System.Numerics;
using RelicTracker.Framework;
using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private RelicOwnership? cachedOwnership;
    private DateTime? cachedOwnershipStamp;

    private void DrawRelicTab()
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

        var line = ResolveDetailSelection(out var job);
        if (line is null)
        {
            ImGui.TextColored(MutedColor, "No relic lines available.");
            return;
        }

        var ownership = GetOwnership();
        var jobList = line.EffectiveJobList;
        var slotIndex = IndexOfJob(jobList, job);

        ImGui.Spacing();
        DrawDetailCollectContext(line, ownership, jobList);
        ImGui.Separator();

        using var scroll = ImRaii.Child("##RelicDetailScroll", new Vector2(0, -1), false);
        if (!scroll)
        {
            return;
        }

        DrawAllJobsGrid(line, jobList, job, ownership);
        ImGui.Spacing();
        DrawDetailSteps(line, job, slotIndex, ownership);
    }

    private RelicOwnership GetOwnership()
    {
        var stamp = ffxivCollect.LastRefreshUtc;
        if (cachedOwnership is null || cachedOwnershipStamp != stamp)
        {
            cachedOwnership = new RelicOwnership(ffxivCollect.Snapshot);
            cachedOwnershipStamp = stamp;
        }

        return cachedOwnership;
    }

    private static int IndexOfJob(IReadOnlyList<string> jobList, string job)
    {
        for (var i = 0; i < jobList.Count; i++)
        {
            if (string.Equals(jobList[i], job, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private RelicLine? ResolveDetailSelection(out string job)
    {
        var expansionId = catalog.Expansions.Contains(config.DetailExpansionId, StringComparer.Ordinal)
            ? config.DetailExpansionId
            : catalog.Expansions.FirstOrDefault() ?? string.Empty;

        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("Expansion##detail", ExpansionLongName(expansionId)))
        {
            foreach (var candidate in catalog.Expansions)
            {
                if (ImGui.Selectable(ExpansionLongName(candidate), candidate == expansionId))
                {
                    config.DetailExpansionId = candidate;
                    config.OnSettingChanged();
                    expansionId = candidate;
                }
            }

            ImGui.EndCombo();
        }

        var lines = catalog.LinesFor(expansionId).ToList();
        var line = lines.FirstOrDefault(candidate => candidate.CollectType == config.DetailCollectType)
                   ?? lines.FirstOrDefault();
        if (line is null)
        {
            job = string.Empty;
            return null;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(190);
        if (ImGui.BeginCombo("Relic##detail", line.CollectType))
        {
            foreach (var candidate in lines)
            {
                if (ImGui.Selectable(candidate.CollectType, candidate.CollectType == line.CollectType))
                {
                    config.DetailCollectType = candidate.CollectType;
                    config.OnSettingChanged();
                    line = candidate;
                }
            }

            ImGui.EndCombo();
        }

        var jobList = line.EffectiveJobList;
        job = jobList.Contains(config.DetailJob, StringComparer.Ordinal)
            ? config.DetailJob
            : jobList.FirstOrDefault() ?? string.Empty;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.BeginCombo("Job##detail", string.IsNullOrEmpty(job) ? "—" : job))
        {
            foreach (var candidate in jobList)
            {
                if (ImGui.Selectable(candidate, candidate == job))
                {
                    config.DetailJob = candidate;
                    config.OnSettingChanged();
                    job = candidate;
                }
            }

            ImGui.EndCombo();
        }

        return line;
    }

    private bool CollectActive =>
        config.FfxivCollectCharacterId != 0 && ffxivCollect.LastRefreshUtc.HasValue;

    private void DrawDetailCollectContext(RelicLine line, RelicOwnership ownership, IReadOnlyList<string> jobList)
    {
        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextColored(MutedColor, "Set a FFXIV Collect ID on the Collect tab to auto-fill finished steps. Until then, tick steps manually.");
            return;
        }

        var complete = 0;
        for (var slot = 0; slot < jobList.Count; slot++)
        {
            if (line.TierCount > 0 && ownership.IsStepDone(line, slot, line.TierCount - 1))
            {
                complete++;
            }
        }

        ImGui.TextColored(GoodColor, "Auto-tracked from FFXIV Collect — no ticking needed.");
        ImGui.SameLine();
        ImGui.TextColored(complete == line.Jobs ? GoodColor : MutedColor, $"({complete}/{line.Jobs} jobs complete)");
        if (ffxivCollect.IsLoading)
        {
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, "syncing…");
        }
    }

    private void DrawAllJobsGrid(RelicLine line, IReadOnlyList<string> jobList, string selectedJob, RelicOwnership ownership)
    {
        if (!ImGui.CollapsingHeader($"All jobs · {line.CollectType}###alljobs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var columns = 1 + jobList.Count;
        using var table = ImRaii.Table(
            "AllJobsGrid",
            columns,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg
                | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY,
            new Vector2(0, Math.Min(320f, (line.TierCount + 2) * ImGui.GetTextLineHeightWithSpacing() + 12f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 150);
        foreach (var jobName in jobList)
        {
            ImGui.TableSetupColumn(jobName, ImGuiTableColumnFlags.WidthFixed, 34);
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        for (var tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{tier + 1}. {line.StepName(tier)}");

            for (var slot = 0; slot < jobList.Count; slot++)
            {
                ImGui.TableNextColumn();
                var done = ownership.IsStepDone(line, slot, tier)
                           || IsManualStepDone(line, jobList[slot], tier);
                var isSelected = string.Equals(jobList[slot], selectedJob, StringComparison.Ordinal);
                var color = done ? GoodColor : isSelected ? WarningColor : MutedColor;
                ImGui.TextColored(color, done ? "✓" : "·");
            }
        }
    }

    private void DrawDetailSteps(RelicLine line, string job, int slotIndex, RelicOwnership ownership)
    {
        var currentTier = CurrentStepTier(line, job, slotIndex, ownership);
        var complete = currentTier >= line.TierCount;

        ImGui.TextColored(HeaderColor, $"{job} · {line.CollectType}");
        ImGui.SameLine();
        if (complete)
        {
            ImGui.TextColored(GoodColor, "— complete");
        }
        else
        {
            ImGui.TextColored(WarningColor, $"— up next: {line.StepName(currentTier)}");
        }

        ImGui.Spacing();
        DrawDetailStepChecklist(line, job, slotIndex, currentTier, ownership, CollectActive);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (complete)
        {
            ImGui.TextColored(GoodColor, $"{job}'s {line.CollectType} relic is finished. Nice.");
            return;
        }

        DrawCurrentStepDetail(line, currentTier);
    }

    private void DrawDetailStepChecklist(RelicLine line, string job, int slotIndex, int currentTier, RelicOwnership ownership, bool collectActive)
    {
        using var table = ImRaii.Table(
            "DetailSteps",
            2,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (var tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            var autoDone = ownership.IsStepDone(line, slotIndex, tier);
            var done = autoDone || IsManualStepDone(line, job, tier);

            ImGui.TableNextColumn();
            if (collectActive)
            {
                // Fully automatic: read-only status, no boxes to tick.
                ImGui.TextColored(done ? GoodColor : MutedColor, done ? "✓" : "·");
                if (done && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(autoDone ? "Completed (from FFXIV Collect)" : "Marked done manually");
                }
            }
            else if (autoDone)
            {
                ImGui.TextColored(GoodColor, "✓");
            }
            else
            {
                var manual = IsManualStepDone(line, job, tier);
                ImGui.PushID(tier);
                if (ImGui.Checkbox("##stepdone", ref manual))
                {
                    SetManualStepDone(line, job, tier, manual);
                }

                ImGui.PopID();
            }

            ImGui.TableNextColumn();
            var isCurrent = tier == currentTier;
            var color = done ? GoodColor : isCurrent ? WarningColor : MutedColor;
            var suffix = isCurrent ? "  ← current step" : string.Empty;
            ImGui.TextColored(color, $"{tier + 1}. {line.StepName(tier)}{suffix}");
        }
    }

    // Catalog step name -> Wyn material-sheet step name, for the few that differ.
    private static readonly Dictionary<string, string> WynStepAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skybuilders'"] = "Skybuilders",
        ["Augmented Dragonsung"] = "Dragonsung",
        ["Augmented Law's Order"] = "Augmented Law's",
    };

    private void DrawCurrentStepDetail(RelicLine line, int currentTier)
    {
        var stepName = line.StepName(currentTier);
        ImGui.TextColored(HeaderColor, $"To do now: {stepName}");
        ImGui.Spacing();

        var note = catalog.StepNote(line.CollectType, stepName);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.TextWrapped(note);
            ImGui.Spacing();
        }

        var items = GetStepItems(line, stepName).ToList();
        if (items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                ImGui.TextWrapped(
                    "No item breakdown recorded for this step — it's mostly tomestones, quests or other tasks. It ticks itself off once FFXIV Collect sees the finished weapon.");
            }

            return;
        }

        ImGui.TextColored(MutedColor, "Materials for one weapon/tool (owned counts are live from Allagan Tools):");
        ImGui.Spacing();

        using var table = ImRaii.Table(
            "DetailStepItems",
            5,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 48);
        ImGui.TableHeadersRow();

        foreach (var item in items)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (item.Resolved)
            {
                ImGui.TextUnformatted(item.Name);
            }
            else
            {
                ImGui.TextColored(WarningColor, item.Name);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Couldn't match this to a game item, so owned can't be counted.");
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(item.Where) ? "—" : item.Where);

            ImGui.TableNextColumn();
            ImGui.Text(item.Need.ToString());

            ImGui.TableNextColumn();
            if (item.Resolved)
            {
                ImGui.Text(item.Owned.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "—");
            }

            ImGui.TableNextColumn();
            var shortfall = item.Need > item.Owned ? item.Need - item.Owned : 0;
            ImGui.TextColored(shortfall == 0 && item.Resolved ? GoodColor : BadColor, item.Resolved ? shortfall.ToString() : "?");
        }
    }

    private readonly record struct StepItem(string Name, string? Where, uint Need, uint Owned, bool Resolved);

    /// <summary>Per-weapon materials for a step, from Wyn's per-expansion data, with live owned counts.</summary>
    private IEnumerable<StepItem> GetStepItems(RelicLine line, string stepName)
    {
        if (!data.Expansions.TryGetValue(line.Expansion, out var sheet))
        {
            yield break;
        }

        var wynStep = WynStepAliases.TryGetValue(stepName, out var alias) ? alias : stepName;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.Materials)
        {
            if (string.IsNullOrWhiteSpace(row.Step)
                || !string.Equals(row.Step.Trim(), wynStep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = row.Material?.Trim();
            if (!MaterialFilters.IsTrackableMaterial(name) || !seen.Add(name!))
            {
                continue;
            }

            var need = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            if (need == 0)
            {
                continue;
            }

            var itemIds = itemResolver.ResolveItemIds(name!);
            var resolved = itemIds.Count > 0;
            var owned = itemIds.Aggregate(0u, (total, itemId) => total + AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly));

            yield return new StepItem(name!, FindLocation(line.Expansion, name!), need, owned, resolved);
        }
    }

    private string? FindLocation(string expansionId, string material)
    {
        foreach (var row in data.GetMaterialReference(expansionId))
        {
            if (string.Equals(row.Material?.Trim(), material, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.Location))
            {
                return row.Location;
            }
        }

        return null;
    }

    private static string StepKey(RelicLine line, string job, int tier) =>
        $"{line.CollectType}|{job}|{tier}";

    private bool IsManualStepDone(RelicLine line, string job, int tier) =>
        config.RelicStepDone.Contains(StepKey(line, job, tier));

    /// <summary>First tier not yet done (auto from Collect or manual) — the step the job is working on.</summary>
    private int CurrentStepTier(RelicLine line, string job, int slotIndex, RelicOwnership ownership)
    {
        for (var tier = 0; tier < line.TierCount; tier++)
        {
            if (!ownership.IsStepDone(line, slotIndex, tier) && !IsManualStepDone(line, job, tier))
            {
                return tier;
            }
        }

        return line.TierCount;
    }

    /// <summary>Manual steps are sequential: ticking fills everything below, unticking clears everything above.</summary>
    private void SetManualStepDone(RelicLine line, string job, int tier, bool done)
    {
        if (done)
        {
            for (var lower = 0; lower <= tier; lower++)
            {
                config.RelicStepDone.Add(StepKey(line, job, lower));
            }
        }
        else
        {
            for (var upper = tier; upper < line.TierCount; upper++)
            {
                config.RelicStepDone.Remove(StepKey(line, job, upper));
            }
        }

        config.OnSettingChanged();
    }
}
