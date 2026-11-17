using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private int? armorGroup;
    private bool showOwnedArmorPieces;
    private readonly Dictionary<ArmorSet, string[]> jobArmorGroups = [];

    private readonly record struct UpgradeTier(string Name, uint ItemId, string Status, bool Complete);

    private void DrawWeaponUpgradeDetail(
        RelicLine weapon, IReadOnlyList<string> jobs, string job, int slot, RelicOwnership ownership)
    {
        int tierCount = VisibleTierCount(weapon);
        if (slot < 0 || tierCount == 0)
        {
            ImGui.TextUnformatted("No upgrades available.");
            return;
        }

        var key = (weapon.CollectType, job, CharacterScope.CurrentContentId);
        if (inspectedRelic != key)
        {
            inspectedRelic = key;
            inspectedTier = null;
        }

        int next = CurrentStepTier(weapon, slot, ownership);
        int selected = Math.Clamp(inspectedTier ?? next, 0, tierCount - 1);
        var tiers = Enumerable.Range(0, tierCount).Select(index =>
        {
            bool complete = ownership.IsStepDoneOrManual(weapon, slot, index);
            return new UpgradeTier(weapon.StepName(index), weapon.RelicId(slot, index),
                complete ? "Complete" : index == next ? "Next upgrade" : "Later upgrade", complete);
        }).ToArray();

        using var series = ImRaii.PushId(weapon.CollectType);
        selected = DrawUpgradeTierRow(tiers, selected);
        inspectedTier = selected == Math.Min(next, tierCount - 1) ? null : selected;
        string stepName = tiers[selected].Name;
        using var pane = ImRaii.Child($"##WeaponUpgrade_{job}_{selected}", new(0, -1), false);
        if (!pane)
        {
            return;
        }

        ImGui.TextColored(HeaderColor, $"{stepName} · {job}");
        if (tiers[selected].Complete)
        {
            ImGui.TextColored(GoodColor, "Upgrade complete");
        }
        else
        {
            if (weapon.CollectType == "Phantom Weapons")
            {
                DrawPhantomWeaponPlan(weapon, stepName, slot, selected, ownership);
            }
            else
            {
                DrawUpgradePlan(weapon, stepName, slot);
            }
        }

        DrawUpgradeQuestDetails(NoteForDiscipline(catalog.StepNote(weapon.CollectType, QuestDetailsStep(weapon, stepName)), slot));
        DrawAllJobsGrid(weapon, jobs, job, ownership);
    }

    private void DrawPhantomWeaponPlan(RelicLine weapon, string stepName, int slot, int tier, RelicOwnership ownership)
    {
        var checklist = ChecklistLines(catalog.StepChecklist(weapon.CollectType, stepName));
        if (stepName == "Occultum")
        {
            DrawRelicChecklist(checklist);
            return;
        }

        var items = GetStepItems(weapon, stepName, slot).ToArray();
        var unlockItems = items.Where(item => item.OneTime).ToArray();
        var upgradeItems = items.Where(item => !item.OneTime).ToArray();
        bool inventoryReady = MogVaultIpc.IsReady;
        if (!inventoryReady && items.Length > 0)
        {
            ImGui.TextWrapped("MogVault is offline. Counts only include items on this character.");
        }

        if (!IsStepUnlockedOnAnyJob(weapon, stepName))
        {
            ImGui.Spacing();
            ImGui.TextColored(HeaderColor, "Unlock once");
            foreach (var objective in checklist.Where(line => line.StartsWith("Once: ", StringComparison.Ordinal)))
            {
                ImGui.TextWrapped(objective[6..]);
            }
            DrawUpgradeItemTasks(unlockItems, inventoryReady);
            if (unlockItems.Any(item => item.Obtain == "craft" && item.Owned < item.Need))
            {
                DrawArtisanCraftButton(weapon, stepName, slot);
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Get materials");
        if (inventoryReady && upgradeItems.Length > 0 && upgradeItems.All(item => item.Resolved && item.Owned >= item.Need))
        {
            ImGui.SameLine();
            ImGui.TextColored(GoodColor, "Materials ready");
        }
        DrawUpgradeItemTasks(upgradeItems, inventoryReady);

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Turn in");
        if (tier == 0)
        {
            ImGui.TextWrapped("Bring the Arcanite to Dodokkuli in Phantom Village.");
        }
        else
        {
            ImGui.TextWrapped($"Bring the Arcanite and your {weapon.StepName(tier - 1)} weapon to Dodokkuli in Phantom Village.");
            if (!ownership.IsStepDoneOrManual(weapon, slot, tier - 1))
            {
                ImGui.TextColored(MutedColor, "The previous upgrade is still incomplete.");
            }
        }
    }

    private void DrawUpgradePlan(RelicLine weapon, string stepName, int slot)
    {
        var items = GetStepItems(weapon, stepName, slot).ToArray();
        bool zodiacStart = weapon.CollectType == "A Relic Reborn" && stepName == "Relic";
        string[] checklist = zodiacStart
            ? ZodiacQuestChecklist(items).ToArray()
            : ChecklistLines(NoteForDiscipline(catalog.StepChecklist(weapon.CollectType, stepName), slot));
        bool inventoryReady = MogVaultIpc.IsReady;
        if (!inventoryReady && items.Length > 0)
        {
            ImGui.TextWrapped("MogVault is offline. Counts only include items on this character.");
        }

        var unlockItems = items.Where(item => item.OneTime).ToArray();
        bool showUnlock = !IsStepUnlockedOnAnyJob(weapon, stepName)
            && (unlockItems.Length > 0 || checklist.Any(line => line.StartsWith("Once: ", StringComparison.Ordinal)));
        bool unlockHasCrafts = showUnlock && unlockItems.Any(item => item.Obtain == "craft");
        if (showUnlock)
        {
            ImGui.TextColored(HeaderColor, "Unlock once");
            foreach (var objective in checklist.Where(line => line.StartsWith("Once: ", StringComparison.Ordinal)))
            {
                ImGui.TextWrapped(objective[6..]);
            }
            DrawUpgradeItemTasks(unlockItems, inventoryReady);
            if (unlockHasCrafts)
            {
                DrawArtisanCraftButton(weapon, stepName, slot);
            }
        }

        var objectives = checklist.Where(line => !line.StartsWith("Once: ", StringComparison.Ordinal))
            .Select(line => line.StartsWith("Per weapon: ", StringComparison.Ordinal) ? line[12..] : line)
            .ToArray();
        bool endsWithTurnIn = objectives.Length > 1
            && (objectives[^1].StartsWith("Return to ", StringComparison.Ordinal)
                || objectives[^1].StartsWith("Bring ", StringComparison.Ordinal)
                || objectives[^1].StartsWith("Hand ", StringComparison.Ordinal)
                || objectives[^1].StartsWith("Deliver ", StringComparison.Ordinal)
                || objectives[^1].StartsWith("Turn ", StringComparison.Ordinal));
        DrawRelicChecklist(endsWithTurnIn ? objectives[..^1] : objectives);
        DrawLucisSalvagerHint(weapon, slot);

        if (data.Expansions.TryGetValue(weapon.Expansion, out var sheet))
        {
            var rewardIndex = ShoppingListBuilder.BuildQuestRewardIndex(sheet);
            DrawStepQuestRewards($"{weapon.CollectType}|{stepName}|Rewards",
                ShoppingListBuilder.GetQuestRewards(stepName, rewardIndex, CreateOwnedLookup(),
                    ExpansionNames.IsTools(weapon.Expansion) ? slot : -1));
        }

        var upgradeItems = items.Where(item => !item.OneTime).ToArray();
        bool craftBesideItem = zodiacStart && upgradeItems.Any(item => item.Obtain == "craft");
        if (upgradeItems.Length > 0)
        {
            ImGui.TextColored(HeaderColor, "Get materials");
            if (inventoryReady && upgradeItems.Any(item => item.Depth == 0)
                && upgradeItems.All(item => item.Depth > 0 || (item.Resolved && item.Owned >= item.Need)))
            {
                ImGui.SameLine();
                ImGui.TextColored(GoodColor, "Materials ready");
            }
            DrawUpgradeItemTasks(upgradeItems, inventoryReady,
                craftBesideItem ? () => DrawArtisanCraftButton(weapon, stepName, slot) : null);
        }
        if (!unlockHasCrafts && !craftBesideItem)
        {
            DrawArtisanCraftButton(weapon, stepName, slot);
        }
        if (endsWithTurnIn)
        {
            ImGui.Spacing();
            ImGui.TextColored(HeaderColor, "Turn in");
            ImGui.TextWrapped(objectives[^1]);
        }
    }

    private static void DrawUpgradeItemTasks(IReadOnlyList<StepItem> items, bool inventoryReady, Action? drawCraft = null)
    {
        foreach (var item in items)
        {
            uint remaining = item.Need > item.Owned ? item.Need - item.Owned : 0;
            bool ready = item.Resolved && inventoryReady && remaining == 0;
            string state = !item.Resolved ? $"{item.Need:N0} needed"
                : ready ? "Ready" : remaining == 0 ? "In bags" : $"{remaining:N0} left";
            float indent = ImGui.GetFontSize() * item.Depth;
            if (indent > 0)
            {
                ImGui.Indent(indent);
            }
            using var id = ImRaii.PushId(item.Name);
            string source = item.IsCraftProduct ? "Turn-in" : item.IsPrecraft ? "Precraft" : item.IsScrip ? "Scrips" : item.Where ?? string.Empty;
            using (var detail = DrawMaterialTask(item.ItemId, item.Name, state, source, ready))
            {
                if (detail)
                {
                    ImGui.TextWrapped(item.Resolved
                        ? $"Need {item.Need:N0} · Have {item.OwnedInventory:N0}"
                        : $"Need {item.Need:N0} · Inventory count unavailable");
                    if (item.OwnedQuestCredit > 0)
                    {
                        ImGui.TextUnformatted($"Covered by quest rewards {item.OwnedQuestCredit:N0}");
                    }
                }
            }
            if (item.Obtain == "craft" && drawCraft is not null)
            {
                drawCraft();
                drawCraft = null;
            }
            if (indent > 0)
            {
                ImGui.Unindent(indent);
            }
        }
    }

    private void DrawArmorUpgradeDetail(ArmorLine armor, RelicOwnership ownership)
    {
        var sets = armor.Sets.Where(set => set.Tiers.Count > 0).ToArray();
        if (sets.Length == 0)
        {
            ImGui.TextUnformatted("No armour sets available.");
            return;
        }

        var key = (armor, CharacterScope.CurrentContentId);
        if (armorSelection != key)
        {
            armorSelection = key;
            showOwnedArmorPieces = false;
            armorSetIndex = Math.Max(0, Array.FindIndex(sets, set =>
                FirstIncompleteArmorTier(set, ownership, DefaultArmorGroup(set)) < set.Tiers.Count));
            armorGroup = DefaultArmorGroup(sets[armorSetIndex]);
            inspectedArmorTier = null;
        }

        ImGui.SetNextItemWidth(Math.Min(190, ImGui.GetContentRegionAvail().X));
        using (var picker = ImRaii.Combo("##ArmorSet", sets[armorSetIndex].Name))
        {
            if (picker)
            {
                for (int index = 0; index < sets.Length; index++)
                {
                    if (ImGui.Selectable(sets[index].Name, armorSetIndex == index))
                    {
                        bool changedGroupType = IsJobArmorSet(sets[index]) != IsJobArmorSet(sets[armorSetIndex]);
                        armorSetIndex = index;
                        if (changedGroupType)
                        {
                            armorGroup = DefaultArmorGroup(sets[index]);
                        }
                        inspectedArmorTier = null;
                    }
                }
            }
        }

        var set = sets[armorSetIndex];
        var groups = ArmorGroups(set);
        string allGroups = IsJobArmorSet(set) ? "All jobs" : "All roles";
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Min(150, ImGui.GetContentRegionAvail().X));
        using (var picker = ImRaii.Combo("##ArmorGroup",
                   armorGroup is int group ? groups[group] : allGroups))
        {
            if (picker)
            {
                if (ImGui.Selectable(allGroups, armorGroup is null))
                {
                    armorGroup = null;
                    inspectedArmorTier = null;
                }
                for (int index = 0; index < groups.Length; index++)
                {
                    if (ImGui.Selectable($"{groups[index]}##{index}", armorGroup == index))
                    {
                        armorGroup = index;
                        inspectedArmorTier = null;
                    }
                }
            }
        }
        ImGui.SameLine();
        ImGui.Checkbox("Show owned", ref showOwnedArmorPieces);

        int next = FirstIncompleteArmorTier(set, ownership, armorGroup);
        int automatic = Math.Min(next, set.Tiers.Count - 1);
        var tiers = set.Tiers.Select((tier, index) =>
        {
            var (owned, total) = ArmorGroupProgress(tier, ownership, armorGroup);
            var (start, count) = ArmorGroupRange(tier, armorGroup);
            uint icon = tier.PieceIds.Skip(start).Take(count).FirstOrDefault(id => id != 0);
            bool complete = total > 0 && owned >= total;
            return new UpgradeTier(tier.Label, icon,
                index == next ? $"{owned}/{total} · Next" : $"{owned}/{total} pieces", complete);
        }).ToArray();

        using var setScope = ImRaii.PushId(set.Name);
        int selected = DrawUpgradeTierRow(tiers, inspectedArmorTier ?? automatic);
        inspectedArmorTier = selected == automatic ? null : selected;
        var tier = set.Tiers[selected];
        using var pane = ImRaii.Child($"##ArmorUpgrade_{tier.CollectType}_{armorGroup}", new(0, -1), false);
        if (!pane)
        {
            return;
        }

        DrawRelicChecklist(ChecklistLines(catalog.StepChecklist(armor.LineName, tier.CollectType)));
        if (MogVaultIpc.IsReady)
        {
            DrawArmorPieceTasks(armor, set, tier, ownership, groups);
        }
        else
        {
            ImGui.TextWrapped("Connect MogVault to see individual piece status.");
        }
        DrawUpgradeQuestDetails(catalog.StepNote(armor.LineName, string.Empty));
    }

    private void DrawArmorPieceTasks(
        ArmorLine armor, ArmorSet set, ArmorTier tier, RelicOwnership ownership, IReadOnlyList<string> groups)
    {
        var (start, count) = ArmorGroupRange(tier, armorGroup);
        string heading = showOwnedArmorPieces ? "Pieces" : "Missing pieces";
        ImGui.TextColored(HeaderColor, heading);
        var exchangeTier = set.Name == ArmorCostCalculator.PhantomVisionSet && tier.Label is "+1" or "+2"
            ? armor.Sets.FirstOrDefault(candidate => candidate.Name == ArmorCostCalculator.ArcanautsSet)?
                .Tiers.FirstOrDefault(candidate => candidate.Label == tier.Label)
            : null;

        bool drewPiece = false;
        int previousRole = -1;
        for (int index = start; index < start + count; index++)
        {
            bool owned = ownership.IsArmorPieceOwned(tier.CollectType, index);
            if (owned && !showOwnedArmorPieces)
            {
                continue;
            }

            int role = index / ArmorCostCalculator.PiecesPerSet;
            if (armorGroup is null && role != previousRole)
            {
                if (previousRole >= 0)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                }

                ImGui.TextColored(ProgressColor, role < groups.Count ? groups[role] : $"Set {role + 1}");
                previousRole = role;
            }

            drewPiece = true;
            uint itemId = index < tier.PieceIds.Count ? tier.PieceIds[index] : 0;
            string name = ItemDisplayNames.Resolve(itemId, $"Piece {index % ArmorCostCalculator.PiecesPerSet + 1}");
            string cost = ArmorPieceCostLabel(armor, set, tier, index);
            using var id = ImRaii.PushId(index);
            using var detail = DrawMaterialTask(itemId, name, owned ? "Owned" : "Missing", owned ? string.Empty : cost, owned);
            if (!detail || owned || exchangeTier is null || index >= exchangeTier.PieceIds.Count)
            {
                continue;
            }

            uint exchangeId = exchangeTier.PieceIds[index];
            if (exchangeId != 0)
            {
                string exchange = ItemDisplayNames.Resolve(exchangeId, "the matching Arcanaut’s piece");
                ImGui.TextWrapped($"Or exchange {exchange} at the North Horn armorer.");
            }
        }

        if (!drewPiece)
        {
            ImGui.TextUnformatted("Every piece in this selection is owned.");
        }
    }

    private static (int Start, int Count) ArmorGroupRange(ArmorTier tier, int? role)
    {
        if (role is not int index)
        {
            return (0, tier.Pieces);
        }

        int start = index * ArmorCostCalculator.PiecesPerSet;
        return (start, Math.Clamp(tier.Pieces - start, 0, ArmorCostCalculator.PiecesPerSet));
    }

    private static (int Owned, int Total) ArmorGroupProgress(ArmorTier tier, RelicOwnership ownership, int? role)
    {
        var (start, count) = ArmorGroupRange(tier, role);
        int owned = role is null ? ownership.OwnedPieceCount(tier.CollectType, tier.Pieces)
            : Enumerable.Range(start, count).Count(index => ownership.IsArmorPieceOwned(tier.CollectType, index));
        return (owned, count);
    }

    private static int FirstIncompleteArmorTier(ArmorSet set, RelicOwnership ownership, int? role)
    {
        int index = set.Tiers.FindIndex(tier =>
        {
            var (owned, total) = ArmorGroupProgress(tier, ownership, role);
            return owned < total;
        });
        return index < 0 ? set.Tiers.Count : index;
    }

    private static bool IsJobArmorSet(ArmorSet set) => set.Name == "Eurekan";

    private int? DefaultArmorGroup(ArmorSet set)
    {
        string? job = Svc.PlayerState.ClassJob.ValueNullable?.Abbreviation.ToString();
        if (!IsJobArmorSet(set))
        {
            return ArmorRoleForJob(job);
        }

        int index = Array.IndexOf(ArmorGroups(set), job);
        return index >= 0 ? index : null;
    }

    private string[] ArmorGroups(ArmorSet set)
    {
        if (!IsJobArmorSet(set))
        {
            return ArmorRoleLabels;
        }
        if (!jobArmorGroups.TryGetValue(set, out var groups))
        {
            groups = set.Tiers[0].PieceIds.Chunk(ArmorCostCalculator.PiecesPerSet)
                .Select((pieces, index) => ClassJobEquipResolver.TryResolveEquipJobByItemId(pieces[0], out var job)
                    ? job : $"Job set {index + 1}")
                .ToArray();
            jobArmorGroups[set] = groups;
        }
        return groups;
    }

    private static int? ArmorRoleForJob(string? job) =>
        job switch
        {
            "PLD" or "WAR" or "DRK" or "GNB" => 0,
            "DRG" or "RPR" => 1,
            "MNK" or "SAM" => 2,
            "BRD" or "MCH" or "DNC" => 3,
            "NIN" or "VPR" => 4,
            "WHM" or "SCH" or "AST" or "SGE" => 5,
            "BLM" or "SMN" or "RDM" or "PCT" or "BLU" => 6,
            _ => null,
        };

    private static int DrawUpgradeTierRow(IReadOnlyList<UpgradeTier> tiers, int selected)
    {
        ImGui.Spacing();
        float cellWidth = tiers.Max(tier => ImGui.CalcTextSize(tier.Name).X)
            + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X + 2 * ImGui.GetStyle().CellPadding.X;
        cellWidth = Math.Max(cellWidth, ImGui.GetFontSize() * 8);
        int columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / cellWidth), 1, tiers.Count);
        using var table = ImRaii.Table("##UpgradeTierRow", columns, ImGuiTableFlags.SizingStretchSame);
        if (!table)
        {
            return selected;
        }

        ImGui.TableNextRow();
        for (int index = 0; index < tiers.Count; index++)
        {
            ImGui.TableNextColumn();
            using var id = ImRaii.PushId(index);
            var tier = tiers[index];
            DrawSeriesIcon(tier.ItemId, ImGui.GetFrameHeight());
            ImGui.SameLine();
            if (ImGui.Selectable(tier.Name, index == selected))
            {
                selected = index;
            }
            ImGui.TextColored(tier.Complete ? GoodColor : MutedColor, tier.Status);
        }
        return selected;
    }

    private void DrawUpgradeQuestDetails(string? note)
    {
        if (!string.IsNullOrWhiteSpace(note) && ImGui.CollapsingHeader("Quest details"))
        {
            DrawNote(note);
        }
    }
}
