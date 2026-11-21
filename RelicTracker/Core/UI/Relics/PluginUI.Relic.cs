using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private int? inspectedTier;
    private (string Series, string Job, ulong Character) inspectedRelic;

    private void DrawRelicTab()
    {
        if (DrawCatalogLoadError())
        {
            return;
        }

        RefreshCollectIfStale();

        var expansion = config.DetailExpansionId;
        var armor = catalog.ArmorLinesFor(expansion).FirstOrDefault(line => line.LineName == config.DetailCollectType);
        var weapon = catalog.LinesFor(expansion).FirstOrDefault(line => line.CollectType == config.DetailCollectType);
        if (ImGui.SmallButton("‹ All Relics"))
        {
            restoreCollectionScroll = true;
            page = Page.Collection;
            return;
        }
        ImGui.SameLine();
        ImGui.TextColored(HeaderColor, armor?.LineName ?? weapon?.CollectType ?? "Upgrade path");
        ImGui.TextColored(MutedColor, ExpansionNames.LongName(expansion));

        var ownership = GetOwnership();

        if (armor is not null)
        {
            DrawRelicArmorStatusChips(armor, ownership);
            EndStickyHeader();
            DrawArmorUpgradeDetail(armor, ownership);
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

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Job");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.BeginCombo("##job-detail", string.IsNullOrEmpty(job) ? "-" : job))
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
        DrawWeaponUpgradeDetail(weapon, jobList, job, slotIndex, ownership);
    }

    private void DrawRelicWeaponStatusChips(RelicLine line, RelicOwnership ownership, IReadOnlyList<string> jobList)
    {
        ImGui.Spacing();
        bool collectLinked = CollectActive;
        bool inventoryLinked = MogVaultIpc.IsReady;

        int tiers = VisibleTierCount(line);
        int complete = 0;
        for (int slot = 0; slot < jobList.Count; slot++)
        {
            if (tiers > 0 && ownership.IsStepDoneOrManual(line, slot, tiers - 1))
            {
                complete++;
            }
        }

        if (inventoryLinked || collectLinked)
        {
            DrawProgressSourceChip(inventoryLinked, collectLinked);
        }
        else
        {
            DrawStatusChip("Saved progress", StatusChipKind.Muted);
        }

        ImGui.SameLine();
        DrawStatusChip($"{complete}/{line.Jobs} jobs", complete == line.Jobs ? StatusChipKind.Ok : StatusChipKind.Muted);

        if (collectLinked && ffxivCollect.IsLoading)
        {
            ImGui.SameLine();
            DrawStatusChip("Syncing…", StatusChipKind.Warn);
        }

        DrawProgressRecheckButton();
    }

    private static string QuestDetailsStep(RelicLine line, string stepName) =>
        line.CollectType == "Eureka Weapons"
            ? stepName switch
            {
                "Base +1" or "Base +2" => "Base",
                "Pagos +1" => "Pagos",
                "Elemental +2" => "Elemental +1",
                "Hydatos +1" or "Base (Physeos)" => "Hydatos",
                _ => stepName,
            }
            : stepName;

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
                if (ownership.IsStepDoneOrManual(line, slot, tier))
                {
                    doneCount++;
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{tier + 1}. {line.StepName(tier)} ({doneCount}/{jobList.Count})");

            for (var slot = 0; slot < jobList.Count; slot++)
            {
                ImGui.TableNextColumn();
                var done = ownership.IsStepDoneOrManual(line, slot, tier);
                var isSelected = string.Equals(jobList[slot], selectedJob, StringComparison.Ordinal);
                var color = done ? GoodColor : isSelected ? WarningColor : MutedColor;
                ImGui.TextColored(color, done ? "✓" : "·");
            }
        }
    }

    private static IEnumerable<string> ZodiacQuestChecklist(IReadOnlyList<StepItem> items)
    {
        var weapon = items.FirstOrDefault(item => item.Obtain == "craft");
        var materia = items.FirstOrDefault(item => item.ObtainVia == "Market Board");
        var oil = items.FirstOrDefault(item => item.ObtainVia == "Tomestones");

        yield return "Follow Gerolt's A Relic Reborn quest for this job.";
        if (weapon.Name is not null && materia.Name is not null)
        {
            yield return $"Turn in the {weapon.Name} melded with {materia.Need} × {materia.Name}.";
        }
        if (oil.Name is not null)
        {
            yield return $"Buy {oil.Name} for 15 Poetics from Auriana in Mor Dhona, then hand it in to Gerolt.";
        }
    }

    private static string[] ChecklistLines(string? checklist) =>
        checklist?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static void DrawRelicChecklist(IEnumerable<string> checklist)
    {
        using var lines = checklist.GetEnumerator();
        if (!lines.MoveNext())
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "What to do");
        do
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped(lines.Current);
        }
        while (lines.MoveNext());

        ImGui.Spacing();
    }

    private void DrawArtisanCraftButton(RelicLine line, string stepName, int slotIndex)
    {
        // Tool lines: one list per crafter class. Weapon lines: the job's own list if the step has
        // one (the Zodiac base weapons), else one list per step across all classes; a one-time
        // step's list goes away once any job is past that step.
        var isTools = ExpansionNames.IsTools(line.Expansion);
        if ((isTools && slotIndex > 7)
            || (!isTools && StepCraftsAreOneTime(line, stepName) && IsStepUnlockedOnAnyJob(line, stepName)))
        {
            return;
        }

        var artisanStep = isTools ? ArtisanIpc.ArtisanStepFor(stepName, slotIndex, GetOnCharacterItemCount) : stepName;
        var craftSlot = isTools || ArtisanIpc.IsPerJobStep(stepName) ? slotIndex : ArtisanIpc.AllClassesSlot;
        if (!ArtisanIpc.TryGetRelicToolListId(artisanStep, craftSlot, out _))
        {
            return;
        }

        var resplendentTier = artisanStep.StartsWith("Resplendent ", StringComparison.Ordinal) ? artisanStep[^1..] : null;
        using (ImRaii.Disabled(ArtisanIpc.IsBusy()))
        {
            if (ImGui.Button(resplendentTier is null ? "Craft with Artisan" : $"Craft Component {resplendentTier} with Artisan"))
            {
                artisanResult = ArtisanIpc.TryStartRelicToolList(artisanStep, craftSlot, out string? error)
                    ? null
                    : new(error ?? "Artisan didn't start the list.", true);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(resplendentTier is not null
                ? $"Crafts Component {resplendentTier} from the Material {resplendentTier} in your bags (up to 30, best case).\n"
                  + "Turn the components in to Limbeth, then press again for the next tier."
                : isTools
                    ? "Starts Artisan's premade list for this step (precrafts and turn-in crafts).\n"
                      + "Vendor mats (scrip, GC seal, gil) are not included - buy those first."
                    : ArtisanIpc.IsPerJobStep(stepName)
                        ? "Starts Artisan's premade list for this job's crafted weapon, precrafts included.\n"
                          + "Meld the materia yourself before handing it in."
                        : "Starts Artisan's premade list for this step's crafts - one weapon's worth, across crafter classes, precrafts included.\n"
                          + "Vendor mats are not included - buy those first.");
        }

        if (artisanResult is not null)
        {
            ImGui.SameLine();
            DrawStatusMessage(ref artisanResult, "artisan_dismiss");
        }
    }

    // The Phantom gate crafts: every crafted material of the step is a one-time item.
    private bool StepCraftsAreOneTime(RelicLine line, string stepName) =>
        data.Expansions.TryGetValue(line.Expansion, out var sheet)
        && sheet.Materials
            .Where(row => row.Obtain == "craft" && string.Equals(row.Step?.Trim(), stepName, StringComparison.OrdinalIgnoreCase))
            .All(row => row.OneTime);

    private IEnumerable<StepItem> GetStepItems(RelicLine line, string stepName, int slotIndex)
    {
        if (!data.Expansions.TryGetValue(line.Expansion, out var sheet))
        {
            yield break;
        }

        // On tool lines the material flag columns line up with the relic job slots, so a Fisher
        // only sees fishing parts and crafters don't see them. Weapon-line rows apply to every job
        // unless they're job-specific (see ShoppingListBuilder.IsJobSpecific).
        var filterBySlot = ExpansionNames.IsTools(line.Expansion);

        var hasFisherSection = filterBySlot && ShoppingListBuilder.ToolStepHasFisherSection(sheet, stepName);
        var oneTimeUnlocked = IsStepUnlockedOnAnyJob(line, stepName);
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

            if (filterBySlot
                    ? !ShoppingListBuilder.ToolMaterialAppliesToSlot(row.Jobs, slotIndex, hasFisherSection)
                    : ShoppingListBuilder.IsJobSpecific(row.Jobs) && !(slotIndex < row.Jobs.Count && row.Jobs[slotIndex] == true))
            {
                continue;
            }

            var name = row.Material?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            if (row.PerUnitCount == 0 || (row.OneTime && oneTimeUnlocked))
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

            var productItem = ToStepItem(row, product, 0, true);
            yield return productItem;
            // Once the turn-in item is crafted, its ingredients and precrafts are no longer needed.
            if (Covered(productItem))
            {
                continue;
            }

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

                var precraftItem = ToStepItem(precraft, precraftName, 1, isPrecraft: true);
                yield return precraftItem;
                if (Covered(precraftItem))
                {
                    continue;
                }

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

        static bool Covered(StepItem item) => item.Resolved && item.OwnedInventory + item.OwnedQuestCredit >= item.Need;

        StepItem ToStepItem(
            ExpansionMaterialRow row,
            string name,
            int depth = 0,
            bool isCraftProduct = false,
            bool isPrecraft = false,
            bool isScrip = false)
        {
            var need = row.PerUnitCount;
            var itemIds = row.MaterialIds;
            var resolved = itemIds.Count > 0;
            var ownedInventory = ShoppingListBuilder.OwnedTowardNeed(itemIds, need, ownedLookup);
            var ownedQuestCredit = ShoppingListBuilder.QuestCreditFor(
                stepName,
                name,
                weaponsCap: 1,
                questIndex,
                ownedLookup);
            var displayName = ItemDisplayNames.Label(itemIds, name);
            var where = data.MaterialSources.TryGetValue(name, out var src) ? src : row.ObtainLabel;
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
                isScrip,
                row.Obtain,
                row.ObtainVia,
                itemIds.FirstOrDefault(id => id != 0),
                row.OneTime);
        }
    }

    private void DrawStepQuestRewards(string configKey, IReadOnlyList<ShoppingQuestRewardRow> rewards)
    {
        var ownedCount = rewards.Count(row => row.Owned > 0);
        DrawQuestRewardsSection(
            configKey,
            rewards,
            $"Prefarmed quest rewards ({ownedCount}/{rewards.Count} in inventory)",
            "Owning these credits their turn-in materials below (for one upgrade).",
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
            ownedColumnWidth: 52,
            showResolvedTooltip: false);
    }

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
        if (start < 0 && wanted == "[[Fisher]]")
        {
            // Some tool notes only split crafters and gatherers; the Fisher follows the gatherer advice there.
            wanted = "[[Gatherers]]";
            start = note.IndexOf(wanted, StringComparison.Ordinal);
        }

        if (start < 0)
        {
            return intro;
        }

        start += wanted.Length;
        var end = note.IndexOf("[[", start, StringComparison.Ordinal);
        var section = (end < 0 ? note[start..] : note[start..end]).Trim();
        return string.IsNullOrEmpty(intro) ? section : $"{intro}\n\n{section}";
    }

    private int VisibleTierCount(RelicLine line) => line.EffectiveTierCount(config.HidePhyseosRelics);

    private int CurrentStepTier(RelicLine line, int slotIndex, RelicOwnership ownership)
    {
        var tiers = VisibleTierCount(line);
        for (var tier = 0; tier < tiers; tier++)
        {
            if (!ownership.IsStepDoneOrManual(line, slotIndex, tier))
            {
                return tier;
            }
        }

        return tiers;
    }

    private void DrawLucisSalvagerHint(RelicLine line, int slotIndex)
    {
        if (!LucisToolShortcut.Applies(line))
        {
            return;
        }

        var ownership = GetOwnership();
        if (ownership.IsStepDoneOrManual(line, slotIndex, line.TierCount - 1))
        {
            return;
        }

        ImGui.PushTextWrapPos(0f);
        if (LucisToolShortcut.IsUnlocked(line, ownership, slotIndex))
        {
            ImGui.TextColored(
                GoodColor,
                $"Skip the grind: buy this Lucis tool from a Calamity Salvager for {LucisToolShortcut.SalvagerGil:N0} gil.");
            ImGui.TextColored(
                MutedColor,
                "Calamity Salvager > Purchase Job-specific Gear > Purchase Lv.50 Arms & Tools. "
                + "Also needs this class's level 50 class quest.");
        }
        else
        {
            var category = LucisToolShortcut.CategoryFor(slotIndex);
            var first = LucisToolShortcut.FirstSlot(line, ownership, category, config.LucisFirstJob.GetValueOrDefault(category));
            var jobs = line.EffectiveJobList;
            ImGui.TextColored(
                MutedColor,
                first == slotIndex || first < 0 || first >= jobs.Count
                    ? $"Tip: once this Lucis tool is done, a Calamity Salvager sells the other {category} Lucis tools "
                      + $"for {LucisToolShortcut.SalvagerGil:N0} gil each."
                    : $"Materials counts {jobs[first]}'s tool as your first {category} Lucis. After that, "
                      + $"buy this tool from a Calamity Salvager for {LucisToolShortcut.SalvagerGil:N0} gil. You can change your first tool in Materials.");
        }

        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private bool IsStepUnlockedOnAnyJob(RelicLine line, string stepName)
    {
        var ownership = GetOwnership();
        for (var tier = 0; tier < line.TierCount; tier++)
        {
            if (!string.Equals(line.StepName(tier), stepName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var slot = 0; slot < line.Jobs; slot++)
            {
                if (ownership.IsStepDoneOrManual(line, slot, tier))
                {
                    return true;
                }
            }
        }

        return false;
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
        bool IsScrip = false,
        string? Obtain = null,
        string? ObtainVia = null,
        uint ItemId = 0,
        bool OneTime = false)
    {
        public uint Owned => OwnedInventory + OwnedQuestCredit;
    }
}
