using RelicTracker.IPC;
using System.Numerics;
namespace RelicTracker;

public sealed partial class PluginUI
{
    // Catalog step name -> Wyn material-sheet step name, for the few that differ.
    // All relic lines now use their catalog step names directly (materials come from the curated
    // tool_extra_materials supplement), so no Wyn step-name aliasing is needed here anymore.
    private static readonly Dictionary<string, string> WynStepAliases = new(StringComparer.OrdinalIgnoreCase);
    private RelicOwnership? cachedOwnership;
    private bool cachedOwnershipActiveCharacterOnly;
    private ulong cachedOwnershipCharacterId;
    private long cachedOwnershipInventoryStamp;
    private DateTime? cachedOwnershipStamp;

    private bool CollectActive =>
        config.FfxivCollectCharacterId != 0 && ffxivCollect.LastRefreshUtc.HasValue;

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

        string expansionId = catalog.Expansions.Contains(config.DetailExpansionId, StringComparer.Ordinal)
            ? config.DetailExpansionId
            : catalog.Expansions.FirstOrDefault() ?? string.Empty;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Expansion");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##expansion-detail", ExpansionLongName(expansionId)))
        {
            foreach (string candidate in catalog.Expansions)
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

        List<RelicLine> weaponLines = [.. catalog.LinesFor(expansionId)];
        List<ArmorLine> armorLines = [.. catalog.ArmorLinesFor(expansionId)];
        if (weaponLines.Count == 0 && armorLines.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No relic lines for this expansion.");
            return;
        }

        ArmorLine? armor = armorLines.FirstOrDefault(candidate => candidate.LineName == config.DetailCollectType);
        RelicLine? weapon = armor is null
            ? weaponLines.FirstOrDefault(candidate => candidate.CollectType == config.DetailCollectType)
            : null;
        if (armor is null && weapon is null)
        {
            weapon = weaponLines.FirstOrDefault();
            armor = weapon is null ? armorLines.FirstOrDefault() : null;
        }

        int relicLineCount = weaponLines.Count + armorLines.Count;
        if (relicLineCount > 1)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Relic");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            string relicLabel = armor is not null ? $"{armor.LineName} (armor)" : weapon?.CollectType ?? "—";
            if (ImGui.BeginCombo("##relic-detail", relicLabel))
            {
                foreach (RelicLine candidate in weaponLines)
                {
                    if (ImGui.Selectable(candidate.CollectType, armor is null && candidate == weapon))
                    {
                        config.DetailCollectType = candidate.CollectType;
                        config.OnSettingChanged();
                        weapon = candidate;
                        armor = null;
                    }
                }

                foreach (ArmorLine candidate in armorLines)
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
        }
        else
        {
            string? soleCollectType = weapon?.CollectType ?? armor?.LineName;
            if (!string.IsNullOrEmpty(soleCollectType) && config.DetailCollectType != soleCollectType)
            {
                config.DetailCollectType = soleCollectType;
                config.OnSettingChanged();
            }
        }

        RelicOwnership ownership = GetOwnership();

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

        IReadOnlyList<string> jobList = weapon.EffectiveJobList;
        string job = jobList.Contains(config.DetailJob, StringComparer.Ordinal)
            ? config.DetailJob
            : jobList.FirstOrDefault() ?? string.Empty;

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Job");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.BeginCombo("##job-detail", string.IsNullOrEmpty(job) ? "—" : job))
        {
            foreach (string candidate in jobList)
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

        int slotIndex = IndexOfJob(jobList, job);

        ImGui.Spacing();
        DrawDetailCollectContext(weapon, ownership, jobList);
        ImGui.Separator();

        using ImRaii.ChildDisposable scroll = ImRaii.Child("##RelicDetailScroll", new(0, -1), false);
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
        int owned = armor.AllTiers.Sum(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces));
        int total = armor.TotalPieces;
        bool complete = total > 0 && owned >= total;

        if (CollectActive)
        {
            ImGui.TextColored(GoodColor, "Auto-tracked from FFXIV Collect — no ticking needed.");
            ImGui.SameLine();
            ImGui.TextColored(complete ? GoodColor : MutedColor, $"({owned}/{total} pieces)");
        }
        else
        {
            ImGui.TextColored(MutedColor, "Tick the pieces you own below. Link FFXIV Collect on the Settings tab to auto-track instead.");
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

        string? note = catalog.StepNote(armor.LineName, string.Empty);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(note);
        }

        ImGui.Spacing();
        using ImRaii.TableDisposable table = ImRaii.Table(
            "ArmorSets",
            3,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Set", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 160);
        ImGui.TableHeadersRow();

        foreach (ArmorSet set in armor.Sets)
        {
            DrawArmorSetRows(set, ownership);
        }
    }

    private void DrawArmorSetRows(ArmorSet set, RelicOwnership ownership)
    {
        bool multiTier = set.Tiers.Count > 1;

        foreach (ArmorTier tier in set.Tiers)
        {
            int tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
            float fraction = tier.Pieces > 0 ? (float)tierOwned / tier.Pieces : 0f;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            // Single-tier sets show just the set name; multi-tier show "Set — Tier".
            string label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, label);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tier.CollectType);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, $"{tierOwned}/{tier.Pieces}");

            ImGui.TableNextColumn();
            if (CollectActive)
            {
                DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");
            }
            else
            {
                // Manual: one checkbox per piece, count of ticked = owned.
                for (int i = 0; i < tier.Pieces; i++)
                {
                    if (i > 0)
                    {
                        ImGui.SameLine();
                    }

                    bool done = config.ArmorPieceDone.Contains($"{tier.CollectType}|{i}");
                    if (ImGui.Checkbox($"##{tier.CollectType}_{i}", ref done))
                    {
                        SetArmorPieceDone(tier.CollectType, i, done);
                    }
                }
            }
        }
    }

    /// <summary>Manual armor piece tick (used when FFXIV Collect isn't linked).</summary>
    private void SetArmorPieceDone(string collectType, int piece, bool done)
    {
        string key = $"{collectType}|{piece}";
        if (done)
        {
            config.ArmorPieceDone.Add(key);
        }
        else
        {
            config.ArmorPieceDone.Remove(key);
        }

        InvalidateOwnershipCache();
        config.OnSettingChanged();
    }

    private RelicOwnership GetOwnership()
    {
        ulong characterId = config.FfxivCollectCharacterId;
        DateTime? stamp = ffxivCollect.LastRefreshUtc;
        long inventoryStamp = AllaganToolsIpc.IsReady ? Environment.TickCount64 / 10_000 : 0;
        if (cachedOwnership is null
            || cachedOwnershipStamp != stamp
            || cachedOwnershipCharacterId != characterId
            || cachedOwnershipInventoryStamp != inventoryStamp
            || cachedOwnershipActiveCharacterOnly != config.ActiveCharacterOnly)
        {
            FfxivCollectSnapshot snapshot = characterId == 0 ? FfxivCollectSnapshot.Empty : ffxivCollect.Snapshot;
            cachedOwnership = new(
                snapshot,
                config.RelicStepDone,
                config.ArmorPieceDone,
                BuildInventoryStepDone());
            cachedOwnershipStamp = stamp;
            cachedOwnershipCharacterId = characterId;
            cachedOwnershipInventoryStamp = inventoryStamp;
            cachedOwnershipActiveCharacterOnly = config.ActiveCharacterOnly;
        }

        return cachedOwnership;
    }

    private HashSet<string> BuildInventoryStepDone()
    {
        HashSet<string> done = new(StringComparer.Ordinal);
        if (!AllaganToolsIpc.IsReady)
        {
            return done;
        }

        foreach (RelicLine line in catalog.Lines)
        {
            IReadOnlyList<string> jobs = line.EffectiveJobList;
            for (int slot = 0; slot < line.Jobs && slot < jobs.Count; slot++)
            {
                for (int tier = 0; tier < line.TierCount; tier++)
                {
                    string? relicName = line.RelicName(slot, tier);
                    if (string.IsNullOrWhiteSpace(relicName)
                        || !itemResolver.TryResolveItem(relicName, out uint itemId)
                        || AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly) == 0)
                    {
                        continue;
                    }

                    for (int completedTier = 0; completedTier <= tier; completedTier++)
                    {
                        done.Add(StepKey(line, jobs[slot], completedTier));
                    }
                }
            }
        }

        return done;
    }

    private void InvalidateOwnershipCache()
    {
        cachedOwnership = null;
        cachedOwnershipStamp = null;
        cachedOwnershipCharacterId = 0;
        cachedOwnershipInventoryStamp = 0;
        cachedOwnershipActiveCharacterOnly = false;
    }

    private static int IndexOfJob(IReadOnlyList<string> jobList, string job)
    {
        for (int i = 0; i < jobList.Count; i++)
        {
            if (string.Equals(jobList[i], job, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void DrawDetailCollectContext(RelicLine line, RelicOwnership ownership, IReadOnlyList<string> jobList)
    {
        if (config.FfxivCollectCharacterId == 0)
        {
            string message = AllaganToolsIpc.IsReady
                ? "Owned relic items are auto-tracked from Allagan Tools. Link FFXIV Collect on the Settings tab or tick missing steps manually."
                : "Set a FFXIV Collect ID on the Settings tab to auto-fill finished steps. Until then, tick steps manually.";
            ImGui.TextColored(MutedColor, message);
            return;
        }

        int complete = 0;
        for (int slot = 0; slot < jobList.Count; slot++)
        {
            if (line.TierCount > 0 && ownership.IsStepDone(line, slot, line.TierCount - 1))
            {
                complete++;
            }
        }

        ImGui.TextColored(GoodColor, AllaganToolsIpc.IsReady
            ? "Auto-tracked from FFXIV Collect and Allagan Tools."
            : "Auto-tracked from FFXIV Collect.");
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
        // Collapsed by default — it's a wide reference grid; open it when you want the full picture.
        if (!ImGui.CollapsingHeader($"All jobs · {line.CollectType}###alljobs"))
        {
            return;
        }

        int columns = 1 + jobList.Count;
        using ImRaii.TableDisposable table = ImRaii.Table(
            "AllJobsGrid",
            columns,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY,
            new(0, Math.Min(320f, (line.TierCount + 2) * ImGui.GetTextLineHeightWithSpacing() + 12f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 150);
        foreach (string jobName in jobList)
        {
            ImGui.TableSetupColumn(jobName, ImGuiTableColumnFlags.WidthFixed, 34);
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        for (int tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{tier + 1}. {line.StepName(tier)}");

            for (int slot = 0; slot < jobList.Count; slot++)
            {
                ImGui.TableNextColumn();
                bool done = ownership.IsStepDone(line, slot, tier)
                            || IsManualStepDone(line, jobList[slot], tier);
                bool isSelected = string.Equals(jobList[slot], selectedJob, StringComparison.Ordinal);
                Vector4 color = done ? GoodColor : isSelected ? WarningColor : MutedColor;
                ImGui.TextColored(color, done ? "✓" : "·");
            }
        }
    }

    private void DrawDetailSteps(RelicLine line, string job, int slotIndex, RelicOwnership ownership)
    {
        int currentTier = CurrentStepTier(line, job, slotIndex, ownership);
        bool complete = currentTier >= line.TierCount;

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
        DrawDetailStepChecklist(line, job, slotIndex, currentTier, ownership);

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

    private void DrawDetailStepChecklist(RelicLine line, string job, int slotIndex, int currentTier, RelicOwnership ownership)
    {
        using ImRaii.TableDisposable table = ImRaii.Table(
            "DetailSteps",
            2,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
            new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (int tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            bool collectDone = ownership.IsCollectStepDone(line, slotIndex, tier);
            bool inventoryDone = ownership.IsInventoryStepDone(line, slotIndex, tier);
            bool autoDone = collectDone || inventoryDone;
            bool manualDone = IsManualStepDone(line, job, tier);
            bool done = autoDone || manualDone;

            ImGui.TableNextColumn();
            if (autoDone)
            {
                ImGui.TextColored(GoodColor, "✓");
                if (ImGui.IsItemHovered())
                {
                    string source = collectDone && inventoryDone
                        ? "FFXIV Collect + Allagan Tools inventory"
                        : collectDone ? "FFXIV Collect" : "Allagan Tools inventory";
                    ImGui.SetTooltip($"Completed ({source})");
                }
            }
            else
            {
                bool manual = manualDone;
                ImGui.PushID(tier);
                if (ImGui.Checkbox("##stepdone", ref manual))
                {
                    SetManualStepDone(line, job, tier, manual);
                }

                ImGui.PopID();
            }

            ImGui.TableNextColumn();
            bool isCurrent = tier == currentTier;
            Vector4 color = done ? GoodColor : isCurrent ? WarningColor : MutedColor;
            string suffix = isCurrent ? "  ← current step" : string.Empty;
            ImGui.TextColored(color, $"{tier + 1}. {line.StepName(tier)}{suffix}");
        }
    }

    private void DrawCurrentStepDetail(RelicLine line, int currentTier, int slotIndex)
    {
        string stepName = line.StepName(currentTier);
        ImGui.TextColored(HeaderColor, $"To do now: {stepName}");
        ImGui.Spacing();

        string? note = NoteForDiscipline(catalog.StepNote(line.CollectType, stepName), slotIndex);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.TextWrapped(note);
            ImGui.Spacing();
        }

        List<StepItem> items = [.. GetStepItems(line, stepName, slotIndex)];
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

        using ImRaii.TableDisposable table = ImRaii.Table(
            "DetailStepItems",
            5,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
            new(0, 0));
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

        foreach (StepItem item in items)
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
            uint shortfall = item.Need > item.Owned ? item.Need - item.Owned : 0;
            ImGui.TextColored(shortfall == 0 && item.Resolved ? GoodColor : BadColor, item.Resolved ? shortfall.ToString() : "?");
        }
    }

    /// <summary>Per-weapon materials for a step, from Wyn's per-expansion data, with live owned counts.</summary>
    private IEnumerable<StepItem> GetStepItems(RelicLine line, string stepName, int slotIndex)
    {
        if (!data.Expansions.TryGetValue(line.Expansion, out ExpansionSheet? sheet))
        {
            yield break;
        }

        // On tool lines the material flag columns line up with the relic job slots, so a Fisher
        // only sees fishing parts and crafters don't see them. Weapon-line flags are spreadsheet
        // artifacts (e.g. every Eureka material is flagged for one stray column), so don't filter.
        bool filterBySlot = string.Equals(line.Expansion, "DoHDoL", StringComparison.Ordinal);

        string wynStep = WynStepAliases.TryGetValue(stepName, out string? alias) ? alias : stepName;
        bool hasFisherSection = filterBySlot && ShoppingListBuilder.ToolStepHasFisherSection(sheet, wynStep);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<uint, uint> ownedCounts = [];
        uint OwnedLookup(uint itemId)
        {
            if (!ownedCounts.TryGetValue(itemId, out uint count))
            {
                count = AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly);
                ownedCounts[itemId] = count;
            }

            return count;
        }

        foreach (ExpansionMaterialRow row in sheet.Materials)
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

            string? name = row.Material?.Trim();
            if (!MaterialFilters.IsTrackableMaterial(name) || !seen.Add(name!))
            {
                continue;
            }

            uint need = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            if (need == 0)
            {
                continue;
            }

            IReadOnlyList<uint> itemIds = itemResolver.ResolveItemIds(name!);
            bool resolved = itemIds.Count > 0;
            uint owned = itemIds.Aggregate(0u, (total, itemId) => total + OwnedLookup(itemId));

            string? where = data.MaterialSources.TryGetValue(name!, out string? src) ? src : null;
            yield return new(name!, where, need, owned, resolved);
        }
    }

    /// <summary>
    ///     Picks the part of a step note relevant to one job. Tool-line notes split per discipline with
    ///     inline [[Crafters]] / [[Gatherers]] / [[Fisher]] tags (slots 0-7 / 8-9 / 10), keeping any
    ///     untagged intro for everyone. Notes without tags (weapons, armor) are returned unchanged.
    /// </summary>
    private static string? NoteForDiscipline(string? note, int slotIndex)
    {
        if (string.IsNullOrEmpty(note) || !note.Contains("[[", StringComparison.Ordinal))
        {
            return note;
        }

        string? wanted = slotIndex switch
        {
            >= 0 and <= 7 => "[[Crafters]]",
            8 or 9 => "[[Gatherers]]",
            10 => "[[Fisher]]",
            var _ => null
        };

        int firstTag = note.IndexOf("[[", StringComparison.Ordinal);
        string intro = note[..firstTag].Trim();

        if (wanted is null)
        {
            return intro;
        }

        int start = note.IndexOf(wanted, StringComparison.Ordinal);
        if (start < 0)
        {
            return intro;
        }

        start += wanted.Length;
        int end = note.IndexOf("[[", start, StringComparison.Ordinal);
        string section = (end < 0 ? note[start..] : note[start..end]).Trim();
        return string.IsNullOrEmpty(intro) ? section : $"{intro}\n\n{section}";
    }

    private static string StepKey(RelicLine line, string job, int tier) =>
        $"{line.CollectType}|{job}|{tier}";

    private bool IsManualStepDone(RelicLine line, string job, int tier) =>
        config.RelicStepDone.Contains(StepKey(line, job, tier));

    /// <summary>First tier not yet done (auto from Collect or manual) — the step the job is working on.</summary>
    private int CurrentStepTier(RelicLine line, string job, int slotIndex, RelicOwnership ownership)
    {
        for (int tier = 0; tier < line.TierCount; tier++)
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
            for (int lower = 0; lower <= tier; lower++)
            {
                config.RelicStepDone.Add(StepKey(line, job, lower));
            }
        }
        else
        {
            for (int upper = tier; upper < line.TierCount; upper++)
            {
                config.RelicStepDone.Remove(StepKey(line, job, upper));
            }
        }

        config.OnSettingChanged();
        InvalidateOwnershipCache();
    }

    private readonly record struct StepItem(string Name, string? Where, uint Need, uint Owned, bool Resolved);
}
