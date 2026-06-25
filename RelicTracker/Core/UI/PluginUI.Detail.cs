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

        var weaponLines = catalog.LinesFor(expansionId).ToList();
        var armorLines = catalog.ArmorLinesFor(expansionId).ToList();
        if (weaponLines.Count == 0 && armorLines.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No relic lines for this expansion.");
            return;
        }

        var armor = armorLines.FirstOrDefault(candidate => candidate.LineName == config.DetailCollectType);
        var weapon = armor is null
            ? weaponLines.FirstOrDefault(candidate => candidate.CollectType == config.DetailCollectType)
            : null;
        if (armor is null && weapon is null)
        {
            weapon = weaponLines.FirstOrDefault();
            armor = weapon is null ? armorLines.FirstOrDefault() : null;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        var relicLabel = armor is not null ? $"{armor.LineName} (armor)" : weapon?.CollectType ?? "—";
        if (ImGui.BeginCombo("Relic##detail", relicLabel))
        {
            foreach (var candidate in weaponLines)
            {
                if (ImGui.Selectable(candidate.CollectType, armor is null && candidate == weapon))
                {
                    config.DetailCollectType = candidate.CollectType;
                    config.OnSettingChanged();
                    weapon = candidate;
                    armor = null;
                }
            }

            foreach (var candidate in armorLines)
            {
                if (ImGui.Selectable($"{candidate.LineName} (armor)", candidate == armor))
                {
                    config.DetailCollectType = candidate.LineName;
                    config.OnSettingChanged();
                    armor = candidate;
                    weapon = null;
                }
            }

            ImGui.EndCombo();
        }

        var ownership = GetOwnership();

        if (armor is not null)
        {
            ImGui.Spacing();
            DrawArmorDetail(armor, ownership);
            return;
        }

        if (weapon is null)
        {
            ImGui.TextColored(MutedColor, "No relic lines available.");
            return;
        }

        var jobList = weapon.EffectiveJobList;
        var job = jobList.Contains(config.DetailJob, StringComparer.Ordinal)
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

        var slotIndex = IndexOfJob(jobList, job);

        ImGui.Spacing();
        DrawDetailCollectContext(weapon, ownership, jobList);
        ImGui.Separator();

        using var scroll = ImRaii.Child("##RelicDetailScroll", new Vector2(0, -1), false);
        if (!scroll)
        {
            return;
        }

        DrawAllJobsGrid(weapon, jobList, job, ownership);
        ImGui.Spacing();
        DrawDetailSteps(weapon, job, slotIndex, ownership);
    }

    private void DrawArmorDetail(ArmorLine armor, RelicOwnership ownership)
    {
        var owned = armor.AllTiers.Sum(tier => Math.Min(tier.Pieces, ownership.OwnedCount(tier.CollectType)));
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextColored(MutedColor, "Set a FFXIV Collect ID on the Collect tab to track armor pieces.");
        }
        else
        {
            ImGui.TextColored(GoodColor, "Auto-tracked from FFXIV Collect — no ticking needed.");
            ImGui.SameLine();
            ImGui.TextColored(complete ? GoodColor : MutedColor, $"({owned}/{total} pieces)");
        }

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(HeaderColor, armor.LineName);
        ImGui.SameLine();
        ImGui.TextColored(complete ? GoodColor : MutedColor, $"— {owned}/{total} pieces");
        if (armor.Sets.Count > 1)
        {
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, $"· {armor.Sets.Count} separate sets");
        }

        var note = catalog.StepNote(armor.LineName, string.Empty);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(note);
        }

        ImGui.Spacing();
        using var table = ImRaii.Table(
            "ArmorSets",
            3,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Set", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 160);
        ImGui.TableHeadersRow();

        foreach (var set in armor.Sets)
        {
            DrawArmorSetRows(set, ownership);
        }
    }

    private void DrawArmorSetRows(ArmorSet set, RelicOwnership ownership)
    {
        var multiTier = set.Tiers.Count > 1;

        foreach (var tier in set.Tiers)
        {
            var tierOwned = Math.Min(tier.Pieces, ownership.OwnedCount(tier.CollectType));
            var fraction = tier.Pieces > 0 ? (float)tierOwned / tier.Pieces : 0f;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            // Single-tier sets show just the set name; multi-tier show "Set — Tier".
            var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, label);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tier.CollectType);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, $"{tierOwned}/{tier.Pieces}");

            ImGui.TableNextColumn();
            DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");
        }
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

        DrawCurrentStepDetail(line, currentTier, slotIndex);
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
        // Skysteel steps now use their catalog names directly via the curated tool_extra_materials
        // supplement, so they no longer need to alias onto Wyn's lumped step names.
        ["Augmented Law's Order"] = "Augmented Law's",
    };

    private void DrawCurrentStepDetail(RelicLine line, int currentTier, int slotIndex)
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

        var items = GetStepItems(line, stepName, slotIndex).ToList();
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
    private IEnumerable<StepItem> GetStepItems(RelicLine line, string stepName, int slotIndex)
    {
        if (!data.Expansions.TryGetValue(line.Expansion, out var sheet))
        {
            yield break;
        }

        // On tool lines the material flag columns line up with the relic job slots, so a Fisher
        // only sees fishing parts and crafters don't see them. Weapon-line flags are spreadsheet
        // artifacts (e.g. every Eureka material is flagged for one stray column), so don't filter.
        var filterBySlot = string.Equals(line.Expansion, "DoHDoL", StringComparison.Ordinal);

        var wynStep = WynStepAliases.TryGetValue(stepName, out var alias) ? alias : stepName;
        var hasFisherSection = filterBySlot && ShoppingListBuilder.ToolStepHasFisherSection(sheet, wynStep);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.Materials)
        {
            if (string.IsNullOrWhiteSpace(row.Step)
                || !string.Equals(row.Step.Trim(), wynStep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filterBySlot && !ShoppingListBuilder.ToolMaterialAppliesToSlot(row.Jobs, slotIndex, hasFisherSection))
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
