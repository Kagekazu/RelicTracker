using RelicTracker.IPC;
using Dalamud.Game.Inventory.InventoryEventArgTypes;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private RelicOwnership? cachedOwnership;
    private ulong cachedLocalContentId;
    private ulong cachedOwnershipCharacterId;
    private long cachedOwnershipInventoryStamp;
    private DateTime? cachedOwnershipStamp;
    private Dictionary<uint, uint>? ownedCountCache;
    private long ownedCountCacheStamp;
    private RelicTrackerDestinationTab? pendingTab;
    private int cacheGeneration;

    private bool CollectActive =>
        config.FfxivCollectCharacterId != 0 && ffxivCollect.LastRefreshUtc.HasValue;

    private bool ArmorAutoTracked => CollectActive || AllaganToolsIpc.IsReady;

    private void DrawRelicTab()
    {
        if (!catalog.IsLoaded || catalog.Lines.Count == 0)
        {
            ImGui.TextColored(WarningColor, "Relic data failed to load. Reload RelicTracker in /xlplugins, or check Dalamud's log.");
            return;
        }

        if (config.FfxivCollectCharacterId != 0)
        {
            ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));
        }

        DrawTabIntro("Per-job steps and notes. Tracker lists farm totals for every unfinished job.");

        var expansionId = catalog.Expansions.Contains(config.DetailExpansionId, StringComparer.Ordinal)
            ? config.DetailExpansionId
            : catalog.Expansions.FirstOrDefault() ?? string.Empty;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Expansion");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##expansion-detail", ExpansionNames.LongName(expansionId)))
        {
            foreach (var candidate in catalog.Expansions)
            {
                if (ImGui.Selectable(ExpansionNames.LongName(candidate), candidate == expansionId))
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
            EndStickyHeader();
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
            DrawRelicArmorStatusChips(armor, ownership);
            EndStickyHeader();
            DrawArmorDetail(armor, ownership);
            return;
        }

        if (weapon is null)
        {
            ImGui.TextColored(MutedColor, "No relic lines available.");
            EndStickyHeader();
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

        DrawRelicWeaponStatusChips(weapon, ownership, jobList);
        EndStickyHeader();

        var slotIndex = IndexOfJob(jobList, job);
        DrawWeaponDetailBody(weapon, jobList, job, slotIndex, ownership);
    }

    private void DrawRelicWeaponStatusChips(RelicLine line, RelicOwnership ownership, IReadOnlyList<string> jobList)
    {
        ImGui.Spacing();
        bool collectLinked = CollectIdLinked;
        bool inventoryLinked = AllaganToolsIpc.IsReady;

        if (!collectLinked && !inventoryLinked)
        {
            DrawStatusChip("Manual", StatusChipKind.Muted);
            ImGui.SameLine();
            DrawProgressSourceHint(ProgressHintContext.RelicDisconnected);
            return;
        }

        int tiers = VisibleTierCount(line);
        int complete = 0;
        for (int slot = 0; slot < jobList.Count; slot++)
        {
            if (tiers > 0 && ownership.IsStepDone(line, slot, tiers - 1))
            {
                complete++;
            }
        }

        if (inventoryLinked && collectLinked)
        {
            DrawStatusChip("Inventory + Collect", StatusChipKind.Ok);
        }
        else if (inventoryLinked)
        {
            DrawStatusChip("Inventory", StatusChipKind.Ok);
        }
        else
        {
            DrawStatusChip("Collect", StatusChipKind.Ok);
        }

        ImGui.SameLine();
        DrawStatusChip($"{complete}/{line.Jobs} jobs", complete == line.Jobs ? StatusChipKind.Ok : StatusChipKind.Muted);

        if (collectLinked && ffxivCollect.IsLoading)
        {
            ImGui.SameLine();
            DrawStatusChip("Syncing…", StatusChipKind.Warn);
        }

        DrawProgressRecheckButton();
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, DescribeWeaponProgressSource(inventoryLinked, collectLinked));
    }

    private void DrawRelicArmorStatusChips(ArmorLine armor, RelicOwnership ownership)
    {
        ImGui.Spacing();
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (ArmorAutoTracked)
        {
            bool inventory = AllaganToolsIpc.IsReady;
            if (inventory && CollectActive)
            {
                DrawStatusChip("Inventory + Collect", StatusChipKind.Ok);
            }
            else if (inventory)
            {
                DrawStatusChip("Inventory", StatusChipKind.Ok);
            }
            else
            {
                DrawStatusChip("Collect", StatusChipKind.Ok);
            }

            ImGui.SameLine();
            DrawStatusChip($"{owned}/{total} pieces", complete ? StatusChipKind.Ok : StatusChipKind.Muted);
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, DescribeArmorProgressSource(inventory, CollectActive));
        }
        else
        {
            DrawStatusChip("Manual", StatusChipKind.Muted);
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, "No auto-tracking yet — expand a set below to tick pieces, or connect Allagan Tools in Settings.");
        }
    }

    private void DrawWeaponDetailBody(
        RelicLine weapon,
        IReadOnlyList<string> jobList,
        string job,
        int slotIndex,
        RelicOwnership ownership)
    {
        var wide = ImGui.GetContentRegionAvail().X >= RelicWideLayoutMinWidth;
        var currentTier = CurrentStepTier(weapon, job, slotIndex, ownership);

        if (wide)
        {
            var gap = 10f;
            var half = (ImGui.GetContentRegionAvail().X - gap) * 0.48f;
            using (var left = ImRaii.Child("##relicLeft", new(half, -1), true))
            {
                if (left)
                {
                    DrawAllJobsGrid(weapon, jobList, job, ownership);
                    ImGui.Spacing();
                    DrawDetailStepsLeft(weapon, job, slotIndex, currentTier, ownership);
                }
            }

            ImGui.SameLine(0, gap);
            using (var right = ImRaii.Child("##relicRight", new(0, -1), true))
            {
                if (right)
                {
                    DrawDetailStepsRight(weapon, currentTier, slotIndex);
                }
            }

            return;
        }

        using var scroll = ImRaii.Child("##RelicDetailScroll", new(0, -1), false);
        if (!scroll)
        {
            return;
        }

        DrawAllJobsGrid(weapon, jobList, job, ownership);
        ImGui.Spacing();
        DrawDetailStepsLeft(weapon, job, slotIndex, currentTier, ownership);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawDetailStepsRight(weapon, currentTier, slotIndex);
    }

    private void DrawArmorDetail(ArmorLine armor, RelicOwnership ownership)
    {
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (BeginPanel("armor_header"))
        {
            ImGui.TextColored(HeaderColor, armor.LineName);
            ImGui.SameLine();
            ImGui.TextColored(complete ? GoodColor : MutedColor, $"— {owned}/{total} pieces");
            if (armor.Sets.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextColored(MutedColor, $"· {armor.Sets.Count} separate sets");
            }

            EndPanel();
        }

        var note = catalog.StepNote(armor.LineName, string.Empty);
        if (!string.IsNullOrWhiteSpace(note))
        {
            if (ImGui.CollapsingHeader("About this armor###armor_about"))
            {
                if (BeginPanel("armor_about_body"))
                {
                    ImGui.TextWrapped(note);
                    EndPanel();
                }
            }
        }

        if (BeginPanel("armor_sets"))
        {
            // Table must End before EndPanel — ending the child first crashes ImGui.
            using (var table = ImRaii.Table(
                "ArmorSets",
                3,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
                new(0, 0)))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Set", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                    ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 160);
                    ImGui.TableHeadersRow();

                    foreach (var set in armor.Sets)
                    {
                        DrawArmorSetRows(set, ownership);
                    }
                }
            }

            EndPanel();
        }

        if (ArmorAutoTracked)
        {
            DrawArmorMissingPieces(armor, ownership);
        }
        else
        {
            foreach (var set in armor.Sets)
            {
                var multiTier = set.Tiers.Count > 1;
                foreach (var tier in set.Tiers)
                {
                    var tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
                    var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
                    if (!ImGui.CollapsingHeader($"{label} ({tierOwned}/{tier.Pieces})###armor_manual_{tier.CollectType}"))
                    {
                        continue;
                    }

                    if (BeginPanel($"armor_ticks_{tier.CollectType}"))
                    {
                        DrawArmorPieceCheckboxes(tier);
                        EndPanel();
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Per-piece list for auto-tracked armor: owned pieces are green, missing stay muted.
    ///     Collect alone is aggregate-only, so without Allagan Tools we only prompt to connect it.
    /// </summary>
    private void DrawArmorMissingPieces(ArmorLine armor, RelicOwnership ownership)
    {
        if (!AllaganToolsIpc.IsReady)
        {
            if (OwnedPieces(armor, ownership) < armor.TotalPieces)
            {
                ImGui.Spacing();
                ImGui.TextColored(
                    MutedColor,
                    "Connect Allagan Tools to list which pieces are missing (Collect only tracks totals).");
            }

            return;
        }

        foreach (var set in armor.Sets)
        {
            var multiTier = set.Tiers.Count > 1;
            foreach (var tier in set.Tiers)
            {
                var namedOwned = CountNamedOwnedArmorPieces(tier, ownership);
                if (namedOwned >= tier.Pieces)
                {
                    continue;
                }

                var missing = tier.Pieces - namedOwned;
                var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
                if (!ImGui.CollapsingHeader(
                        $"Pieces — {label} ({namedOwned}/{tier.Pieces}, {missing} left)###armor_pieces_{tier.CollectType}"))
                {
                    continue;
                }

                if (BeginPanel($"armor_pieces_body_{tier.CollectType}"))
                {
                    DrawArmorPieceStatusList(tier, ownership);
                    EndPanel();
                }
            }
        }
    }

    private static int CountNamedOwnedArmorPieces(ArmorTier tier, RelicOwnership ownership)
    {
        var owned = 0;
        var count = Math.Min(tier.Pieces, tier.PieceIds.Count);
        for (var i = 0; i < count; i++)
        {
            if (ownership.IsArmorPieceOwned(tier.CollectType, i))
            {
                owned++;
            }
        }

        return owned;
    }

    private static void DrawArmorPieceStatusList(ArmorTier tier, RelicOwnership ownership)
    {
        const int slotsPerRole = 5;
        string[] roleLabels = ["Fending", "Maiming", "Striking", "Aiming", "Scouting", "Healing", "Casting"];
        var count = Math.Min(tier.Pieces, tier.PieceIds.Count);

        for (var i = 0; i < count; i++)
        {
            if (i % slotsPerRole == 0)
            {
                var roleIndex = i / slotsPerRole;
                var role = roleIndex < roleLabels.Length ? roleLabels[roleIndex] : $"Set {roleIndex + 1}";
                ImGui.TextColored(MutedColor, role);
            }

            var owned = ownership.IsArmorPieceOwned(tier.CollectType, i);
            var pieceId = tier.PieceIds[i];
            var name = ItemDisplayNames.Resolve(pieceId, $"Piece {i + 1}");
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored(owned ? GoodColor : MutedColor, name);
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
            DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");
        }
    }

    /// <summary>One checkbox per piece, wrapped by role set (5 slots) with role headers.</summary>
    private void DrawArmorPieceCheckboxes(ArmorTier tier)
    {
        const int slotsPerRole = 5;
        string[] roleLabels = ["Fending", "Maiming", "Striking", "Aiming", "Scouting", "Healing", "Casting"];

        for (var i = 0; i < tier.Pieces; i++)
        {
            if (i % slotsPerRole == 0)
            {
                var roleIndex = i / slotsPerRole;
                var role = roleIndex < roleLabels.Length ? roleLabels[roleIndex] : $"Set {roleIndex + 1}";
                ImGui.TextColored(MutedColor, role);
            }
            else
            {
                ImGui.SameLine();
            }

            bool done = config.CurrentCharacterProgress().ArmorPieceDone.Contains($"{tier.CollectType}|{i}");
            if (ImGui.Checkbox($"##{tier.CollectType}_{i}", ref done))
            {
                SetArmorPieceDone(tier.CollectType, i, done);
            }

            if (ImGui.IsItemHovered() && i < tier.PieceIds.Count && tier.PieceIds[i] != 0)
            {
                ImGui.SetTooltip(ItemDisplayNames.Resolve(tier.PieceIds[i], $"Piece {i + 1}"));
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
            HashSet<string> inventoryArmorDone;
            if (AllaganToolsIpc.IsReady)
            {
                Func<uint, uint> ownedLookup = CreateOwnedLookup();
                inventoryDone = InventoryProgressBuilder.BuildStepDoneKeys(catalog, ownedLookup);
                inventoryArmorDone = InventoryProgressBuilder.BuildArmorPieceDoneKeys(catalog, ownedLookup);
                config.SaveInventorySnapshot(inventoryDone, inventoryArmorDone);
            }
            else
            {
                inventoryDone = new HashSet<string>(progress.InventoryStepDone, StringComparer.Ordinal);
                inventoryArmorDone = new HashSet<string>(progress.InventoryArmorPieceDone, StringComparer.Ordinal);
            }

            cachedOwnership = new(
                snapshot,
                progress.RelicStepDone,
                progress.ArmorPieceDone,
                inventoryDone,
                inventoryArmorDone);
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
        cacheGeneration++;
        InvalidateShoppingCache();
        InvalidateOwnedCountCache();
    }

    public void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> _) => InvalidateOwnershipCache();

    public void OpenTo(RelicItemTarget target)
    {
        config.SelectedExpansionId = target.ExpansionId;
        config.DetailExpansionId = target.ExpansionId;
        if (!string.IsNullOrEmpty(target.CollectType))
        {
            config.DetailCollectType = target.CollectType;
        }

        if (!string.IsNullOrEmpty(target.Job))
        {
            config.DetailJob = target.Job;
        }

        if (target.Tab == RelicTrackerDestinationTab.Tracker)
        {
            config.TrackerLineFilter = string.Empty;
        }

        pendingTab = target.Tab;
        config.OnSettingChanged();
        IsOpen = true;
    }

    public void OnCharacterChanged()
    {
        config.MigrateLegacyProgressIfNeeded();
        InvalidateOwnershipCache();
    }

    public void OnCharacterLoggedOut(int type, int code) => InvalidateOwnershipCache();

    private ImGuiTabItemFlags TabOpenFlags(RelicTrackerDestinationTab tab) =>
        pendingTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

    private void ConsumePendingTab(RelicTrackerDestinationTab tab)
    {
        if (pendingTab == tab)
        {
            pendingTab = null;
        }
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

    private void DrawAllJobsGrid(RelicLine line, IReadOnlyList<string> jobList, string selectedJob, RelicOwnership ownership)
    {
        // Collapsed by default — it's a wide reference grid; open it when you want the full picture.
        if (!ImGui.CollapsingHeader($"All jobs · {line.CollectType}###alljobs"))
        {
            return;
        }

        var tiers = VisibleTierCount(line);
        var columns = 1 + jobList.Count;
        using var table = ImRaii.Table(
            "AllJobsGrid",
            columns,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY,
            new(0, Math.Min(320f, (tiers + 2) * ImGui.GetTextLineHeightWithSpacing() + 12f)));
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

        for (var tier = 0; tier < tiers; tier++)
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

    private void DrawDetailStepsLeft(
        RelicLine line,
        string job,
        int slotIndex,
        int currentTier,
        RelicOwnership ownership)
    {
        var tiers = VisibleTierCount(line);
        var complete = currentTier >= tiers;

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

        if (complete)
        {
            ImGui.Spacing();
            ImGui.TextColored(GoodColor, $"{job}'s {line.CollectType} relic is finished. Nice.");
            return;
        }

        var stepName = line.StepName(currentTier);
        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, $"To do now: {stepName}");
        ImGui.Spacing();

        var note = NoteForDiscipline(catalog.StepNote(line.CollectType, stepName), slotIndex);
        if (!string.IsNullOrWhiteSpace(note))
        {
            ImGui.TextWrapped(note);
        }
    }

    private void DrawDetailStepsRight(RelicLine line, int currentTier, int slotIndex)
    {
        if (currentTier >= VisibleTierCount(line))
        {
            return;
        }

        var stepName = line.StepName(currentTier);
        DrawArtisanCraftButton(line, stepName, slotIndex);

        if (data.Expansions.TryGetValue(line.Expansion, out var expansionSheet))
        {
            var questIndex = ShoppingListBuilder.BuildQuestRewardIndex(expansionSheet);
            DrawStepQuestRewards(
                $"{line.CollectType}|{stepName}|Rewards",
                ShoppingListBuilder.GetQuestRewards(stepName, questIndex, CreateOwnedLookup()));
        }

        List<StepItem> items = [.. GetStepItems(line, stepName, slotIndex)];
        if (items.Count == 0)
        {
            var note = NoteForDiscipline(catalog.StepNote(line.CollectType, stepName), slotIndex);
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
        DrawDetailStepItemsTable(items);
    }

    private void DrawDetailStepItemsTable(IReadOnlyList<StepItem> items)
    {
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
                ImGui.Text(item.OwnedInventory.ToString());
                if (item.OwnedQuestCredit > 0 && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        $"+{item.OwnedQuestCredit} from prefarmed quest rewards in inventory.\n"
                        + $"Effective owned: {item.Owned}.");
                }
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

        var tiers = VisibleTierCount(line);
        for (var tier = 0; tier < tiers; tier++)
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
                        ? "From inventory + Collect"
                        : collectDone
                            ? "From FFXIV Collect"
                            : "From inventory";
                    ImGui.SetTooltip(source);
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

    /// <summary>Per-weapon materials for a step from bundled expansion data, with live owned counts.</summary>
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
        var questIndex = ShoppingListBuilder.BuildQuestRewardIndex(sheet);

        List<ExpansionMaterialRow> matched = [];
        foreach (var row in sheet.Materials)
        {
            if (string.IsNullOrWhiteSpace(row.Step)
                || !string.Equals(row.Step.Trim(), stepName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ShoppingListBuilder.IsQuestRewardRow(row))
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
            var itemIds = row.MaterialIds;
            var resolved = itemIds.Count > 0;
            var ownedInventory = ShoppingListBuilder.SumOwned(itemIds, ownedLookup);
            var ownedQuestCredit = ShoppingListBuilder.QuestCreditFor(
                stepName,
                name,
                weaponsCap: 1,
                questIndex,
                ownedLookup);
            var displayName = ItemDisplayNames.Label(itemIds, name);
            var where = data.MaterialSources.TryGetValue(name, out var src) ? src : null;
            return new(
                displayName,
                where,
                need,
                ownedInventory,
                ownedQuestCredit,
                resolved,
                depth,
                isCraftProduct,
                isPrecraft,
                isScrip);
        }
    }

    private void DrawStepQuestRewards(string configKey, IReadOnlyList<ShoppingQuestRewardRow> rewards)
    {
        if (rewards.Count == 0)
        {
            return;
        }

        var ownedCount = rewards.Count(row => row.Owned > 0);
        if (!DrawCollapsingSection(
                configKey,
                $"Prefarmed quest rewards ({ownedCount}/{rewards.Count} in inventory)",
                ownedCount > 0))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Owning these credits their turn-in materials below (one weapon).");
        ImGui.Spacing();

        using var table = ImRaii.Table($"RelicQuestRewards_{configKey}", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 52);
        ImGui.TableHeadersRow();

        foreach (var reward in rewards)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (reward.Resolved)
            {
                ImGui.TextUnformatted(reward.DisplayMaterial);
            }
            else
            {
                ImGui.TextColored(WarningColor, reward.DisplayMaterial);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(reward.Owned > 0 ? GoodColor : MutedColor, reward.Resolved ? reward.Owned.ToString() : "—");
        }

        ImGui.Spacing();
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

    private int VisibleTierCount(RelicLine line) => line.EffectiveTierCount(config.HidePhyseosRelics);

    /// <summary>First tier not yet done (auto from Collect or manual) — the step the job is working on.</summary>
    private int CurrentStepTier(RelicLine line, string job, int slotIndex, RelicOwnership ownership)
    {
        var tiers = VisibleTierCount(line);
        for (var tier = 0; tier < tiers; tier++)
        {
            if (!ownership.IsStepDone(line, slotIndex, tier) && !IsManualStepDone(line, job, tier))
            {
                return tier;
            }
        }

        return tiers;
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
        uint OwnedInventory,
        uint OwnedQuestCredit,
        bool Resolved,
        int Depth = 0,
        bool IsCraftProduct = false,
        bool IsPrecraft = false,
        bool IsScrip = false)
    {
        public uint Owned => OwnedInventory + OwnedQuestCredit;
    }
}
