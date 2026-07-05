using RelicTracker.Framework;
using RelicTracker.IPC;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private RelicOwnership? cachedOwnership;
    private ulong cachedLocalContentId;
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

        var expansionId = catalog.Expansions.Contains(config.DetailExpansionId, StringComparer.Ordinal)
            ? config.DetailExpansionId
            : catalog.Expansions.FirstOrDefault() ?? string.Empty;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Expansion");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##expansion-detail", ExpansionLongName(expansionId)))
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

        List<RelicLine> weaponLines = [.. catalog.LinesFor(expansionId)];
        List<ArmorLine> armorLines = [.. catalog.ArmorLinesFor(expansionId)];
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

        var relicLineCount = weaponLines.Count + armorLines.Count;
        if (relicLineCount > 1)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Relic");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            var relicLabel = armor is not null ? $"{armor.LineName} (armor)" : weapon?.CollectType ?? "—";
            if (ImGui.BeginCombo("##relic-detail", relicLabel))
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
        }
        else
        {
            var soleCollectType = weapon?.CollectType ?? armor?.LineName;
            if (!string.IsNullOrEmpty(soleCollectType) && config.DetailCollectType != soleCollectType)
            {
                config.DetailCollectType = soleCollectType;
                config.OnSettingChanged();
            }
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
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Job");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.BeginCombo("##job-detail", string.IsNullOrEmpty(job) ? "—" : job))
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

        using var scroll = ImRaii.Child("##RelicDetailScroll", new(0, -1), false);
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
        var owned = armor.AllTiers.Sum(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces));
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (CollectActive)
        {
            ImGui.TextColored(GoodColor, "Auto-tracked from FFXIV Collect — no ticking needed.");
            ImGui.SameLine();
            ImGui.TextColored(complete ? GoodColor : MutedColor, $"({owned}/{total} pieces)");
        }
        else
        {
            ImGui.TextColored(MutedColor, "Tick the pieces you own below, or link FFXIV Collect on Settings to auto-track.");
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
            new(0, 0));
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
            var tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
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
            if (CollectActive)
            {
                DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");
            }
            else
            {
                // Manual: one checkbox per piece, count of ticked = owned.
                for (var i = 0; i < tier.Pieces; i++)
                {
                    if (i > 0)
                    {
                        ImGui.SameLine();
                    }

                    bool done = config.CurrentCharacterProgress().ArmorPieceDone.Contains($"{tier.CollectType}|{i}");
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
        HashSet<string> armor = config.CurrentCharacterProgress().ArmorPieceDone;
        if (done)
        {
            armor.Add(key);
        }
        else
        {
            armor.Remove(key);
        }

        InvalidateOwnershipCache();
        config.OnSettingChanged();
    }

    private RelicOwnership GetOwnership()
    {
        ulong collectCharacterId = config.FfxivCollectCharacterId;
        ulong localContentId = CharacterScope.CurrentContentId;
        DateTime? stamp = ffxivCollect.LastRefreshUtc;
        long inventoryStamp = InventoryCacheStamp();
        if (cachedOwnership is null
            || cachedOwnershipStamp != stamp
            || cachedOwnershipCharacterId != collectCharacterId
            || cachedLocalContentId != localContentId
            || cachedOwnershipInventoryStamp != inventoryStamp)
        {
            FfxivCollectSnapshot snapshot = collectCharacterId == 0 ? FfxivCollectSnapshot.Empty : ffxivCollect.Snapshot;
            CharacterProgress progress = config.CurrentCharacterProgress();
            HashSet<string> inventoryDone;
            if (AllaganToolsIpc.IsReady)
            {
                inventoryDone = InventoryProgressBuilder.BuildStepDoneKeys(catalog, itemResolver, CreateOwnedLookup());
                config.SaveInventorySnapshot(inventoryDone);
            }
            else
            {
                inventoryDone = new HashSet<string>(progress.InventoryStepDone, StringComparer.Ordinal);
            }

            cachedOwnership = new(
                snapshot,
                progress.RelicStepDone,
                progress.ArmorPieceDone,
                inventoryDone);
            cachedOwnershipStamp = stamp;
            cachedOwnershipCharacterId = collectCharacterId;
            cachedLocalContentId = localContentId;
            cachedOwnershipInventoryStamp = inventoryStamp;
        }

        return cachedOwnership;
    }

    private void InvalidateOwnershipCache()
    {
        cachedOwnership = null;
        cachedOwnershipStamp = null;
        cachedOwnershipCharacterId = 0;
        cachedLocalContentId = 0;
        cachedOwnershipInventoryStamp = 0;
    }

    public void OnCharacterChanged()
    {
        config.MigrateLegacyProgressIfNeeded();
        InvalidateOwnershipCache();
    }

    public void OnCharacterLoggedOut(int type, int code) => InvalidateOwnershipCache();

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

    private void DrawDetailCollectContext(RelicLine line, RelicOwnership ownership, IReadOnlyList<string> jobList)
    {
        bool collectLinked = CollectIdLinked;
        bool inventoryLinked = AllaganToolsIpc.IsReady;

        if (!collectLinked && !inventoryLinked)
        {
            DrawProgressSourceHint(ProgressHintContext.RelicDisconnected);
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

        ImGui.TextColored(GoodColor, DescribeWeaponProgressSource(inventoryLinked, collectLinked));
        ImGui.SameLine();
        ImGui.TextColored(complete == line.Jobs ? GoodColor : MutedColor, $"({complete}/{line.Jobs} jobs complete)");

        if (collectLinked && ffxivCollect.IsLoading)
        {
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, "syncing…");
        }

        DrawProgressRecheckButton();
    }

    private void DrawAllJobsGrid(RelicLine line, IReadOnlyList<string> jobList, string selectedJob, RelicOwnership ownership)
    {
        // Collapsed by default — it's a wide reference grid; open it when you want the full picture.
        if (!ImGui.CollapsingHeader($"All jobs · {line.CollectType}###alljobs"))
        {
            return;
        }

        var columns = 1 + jobList.Count;
        using var table = ImRaii.Table(
            "AllJobsGrid",
            columns,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY,
            new(0, Math.Min(320f, (line.TierCount + 2) * ImGui.GetTextLineHeightWithSpacing() + 12f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthFixed, 200);
        foreach (var jobName in jobList)
        {
            ImGui.TableSetupColumn(jobName, ImGuiTableColumnFlags.WidthFixed, 34);
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        for (var tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            var doneCount = 0;
            for (var slot = 0; slot < jobList.Count; slot++)
            {
                if (ownership.IsStepDone(line, slot, tier) || IsManualStepDone(line, jobList[slot], tier))
                {
                    doneCount++;
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{tier + 1}. {line.StepName(tier)} ({doneCount}/{jobList.Count})");

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
        using var table = ImRaii.Table(
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

        for (var tier = 0; tier < line.TierCount; tier++)
        {
            ImGui.TableNextRow();

            var collectDone = ownership.IsCollectStepDone(line, slotIndex, tier);
            var inventoryDone = ownership.IsInventoryStepDone(line, slotIndex, tier);
            var autoDone = collectDone || inventoryDone;
            var manualDone = IsManualStepDone(line, job, tier);
            var done = autoDone || manualDone;

            ImGui.TableNextColumn();
            if (autoDone)
            {
                ImGui.TextColored(GoodColor, "✓");
                if (ImGui.IsItemHovered())
                {
                    string source = collectDone && inventoryDone
                        ? "FFXIV Collect + Allagan Tools (replicas count)"
                        : collectDone
                            ? "FFXIV Collect"
                            : "Allagan Tools inventory (replicas count)";
                    ImGui.SetTooltip($"Completed ({source})");
                }
            }
            else
            {
                var manual = manualDone;
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

    private void DrawCurrentStepDetail(RelicLine line, int currentTier, int slotIndex)
    {
        var stepName = line.StepName(currentTier);
        ImGui.TextColored(HeaderColor, $"To do now: {stepName}");
        ImGui.Spacing();

        var note = NoteForDiscipline(catalog.StepNote(line.CollectType, stepName), slotIndex);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.TextWrapped(note);
            ImGui.Spacing();
        }

        DrawArtisanCraftButton(line, stepName, slotIndex);

        List<StepItem> items = [.. GetStepItems(line, stepName, slotIndex)];
        if (items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                ImGui.TextWrapped(
                    "No item breakdown recorded for this step — it's mostly tomestones, quests or other tasks. "
                    + "It ticks off when you own the finished relic (Allagan Tools), link FFXIV Collect, or tick it manually.");
            }

            return;
        }

        if (AllaganToolsIpc.IsReady)
        {
            ImGui.TextColored(MutedColor, "Materials for one weapon/tool (owned counts from Allagan Tools):");
        }
        else
        {
            ImGui.TextColored(MutedColor, "Materials for one weapon/tool (connect Allagan Tools on Settings for owned counts):");
        }

        ImGui.Spacing();

        using var table = ImRaii.Table(
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

        foreach (var item in items)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (item.Depth > 0)
            {
                ImGui.Indent(24f * item.Depth);
            }

            var displayName = item.Depth > 0 ? $"- {item.Name}" : item.Name;
            if (item.Resolved)
            {
                if (item.IsCraftProduct)
                {
                    ImGui.TextColored(HeaderColor, displayName);
                }
                else if (item.Depth >= 2)
                {
                    ImGui.TextColored(MutedColor, displayName);
                }
                else if (item.IsScrip)
                {
                    ImGui.TextColored(MutedColor, displayName);
                }
                else
                {
                    ImGui.TextUnformatted(displayName);
                }
            }
            else
            {
                ImGui.TextColored(WarningColor, displayName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Couldn't match this to a game item, so owned can't be counted.");
                }
            }

            if (item.Depth > 0)
            {
                ImGui.Unindent(24f * item.Depth);
            }

            ImGui.TableNextColumn();
            if (item.IsCraftProduct)
            {
                ImGui.TextColored(MutedColor, "Collectable");
            }
            else if (item.IsPrecraft)
            {
                ImGui.TextColored(MutedColor, "Precraft");
            }
            else if (item.IsScrip)
            {
                ImGui.TextColored(MutedColor, "Scrip");
            }
            else
            {
                ImGui.TextWrapped(string.IsNullOrWhiteSpace(item.Where) ? "—" : item.Where);
            }

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

        var hasFisherSection = filterBySlot && ShoppingListBuilder.ToolStepHasFisherSection(sheet, stepName);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Func<uint, uint> ownedLookup = CreateOwnedLookup();

        List<ExpansionMaterialRow> matched = [];
        foreach (var row in sheet.Materials)
        {
            if (string.IsNullOrWhiteSpace(row.Step)
                || !string.Equals(row.Step.Trim(), stepName, StringComparison.OrdinalIgnoreCase))
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

            matched.Add(row);
        }

        foreach (var row in matched)
        {
            if (!string.Equals(row.Role, "craft", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var product = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(product))
            {
                continue;
            }

            yield return ToStepItem(row, product, 0, true);

            foreach (var ingredient in matched)
            {
                if (!string.Equals(ingredient.CraftOf, product, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ingredient.Role, "precraft", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ingredientName = ingredient.Material?.Trim();
                if (string.IsNullOrWhiteSpace(ingredientName))
                {
                    continue;
                }

                var isScrip = string.Equals(ingredient.Role, "scrip", StringComparison.OrdinalIgnoreCase)
                              || ingredientName.StartsWith("Select ", StringComparison.OrdinalIgnoreCase)
                              || ingredientName.StartsWith("Oddly Specific ", StringComparison.OrdinalIgnoreCase)
                              || ingredientName.StartsWith("Oddly Delicate ", StringComparison.OrdinalIgnoreCase);
                yield return ToStepItem(ingredient, ingredientName, 1, isScrip: isScrip);
            }

            foreach (var precraft in matched)
            {
                if (!string.Equals(precraft.CraftOf, product, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(precraft.Role, "precraft", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var precraftName = precraft.Material?.Trim();
                if (string.IsNullOrWhiteSpace(precraftName))
                {
                    continue;
                }

                yield return ToStepItem(precraft, precraftName, 1, isPrecraft: true);

                foreach (var raw in matched)
                {
                    if (!string.Equals(raw.CraftOf, precraftName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var rawName = raw.Material?.Trim();
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        continue;
                    }

                    yield return ToStepItem(raw, rawName, 2);
                }
            }
        }

        foreach (var row in matched)
        {
            if (string.Equals(row.Role, "craft", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(row.CraftOf))
            {
                continue;
            }

            var name = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return ToStepItem(row, name);
        }

        StepItem ToStepItem(
            ExpansionMaterialRow row,
            string name,
            int depth = 0,
            bool isCraftProduct = false,
            bool isPrecraft = false,
            bool isScrip = false)
        {
            var need = (uint)Math.Max(0, Math.Round(row.PerUnit ?? 0));
            var itemIds = itemResolver.ResolveItemIds(name);
            var resolved = itemIds.Count > 0;
            var owned = itemIds.Aggregate(0u, (total, itemId) => total + ownedLookup(itemId));
            var where = data.MaterialSources.TryGetValue(name, out var src) ? src : null;
            return new(name, where, need, owned, resolved, depth, isCraftProduct, isPrecraft, isScrip);
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

        var wanted = slotIndex switch
        {
            >= 0 and <= 7 => "[[Crafters]]",
            8 or 9 => "[[Gatherers]]",
            10 => "[[Fisher]]",
            var _ => null
        };

        var firstTag = note.IndexOf("[[", StringComparison.Ordinal);
        var intro = note[..firstTag].Trim();

        if (wanted is null)
        {
            return intro;
        }

        var start = note.IndexOf(wanted, StringComparison.Ordinal);
        if (start < 0)
        {
            return intro;
        }

        start += wanted.Length;
        var end = note.IndexOf("[[", start, StringComparison.Ordinal);
        var section = (end < 0 ? note[start..] : note[start..end]).Trim();
        return string.IsNullOrEmpty(intro) ? section : $"{intro}\n\n{section}";
    }

    private static string StepKey(RelicLine line, string job, int tier) =>
        $"{line.CollectType}|{job}|{tier}";

    private bool IsManualStepDone(RelicLine line, string job, int tier) =>
        config.CurrentCharacterProgress().RelicStepDone.Contains(StepKey(line, job, tier));

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
        HashSet<string> steps = config.CurrentCharacterProgress().RelicStepDone;
        if (done)
        {
            for (int lower = 0; lower <= tier; lower++)
            {
                steps.Add(StepKey(line, job, lower));
            }
        }
        else
        {
            for (int upper = tier; upper < line.TierCount; upper++)
            {
                steps.Remove(StepKey(line, job, upper));
            }
        }

        config.OnSettingChanged();
        InvalidateOwnershipCache();
    }

    private readonly record struct StepItem
    (
        string Name,
        string? Where,
        uint Need,
        uint Owned,
        bool Resolved,
        int Depth = 0,
        bool IsCraftProduct = false,
        bool IsPrecraft = false,
        bool IsScrip = false);
}
