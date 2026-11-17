using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private readonly record struct TrackerActivity(string Line, string Step, int JobsLeft, string? Note);

    private StatusMessage? gatherChillResult;

    // Inventory is counted once per material, not summed per row.
    private static List<ShoppingMaterialRow> MergeByMaterial(IReadOnlyList<ShoppingMaterialRow> rows)
    {
        List<ShoppingMaterialRow> merged = [];
        foreach (var group in rows
            .GroupBy(row => (row.Material.ToLowerInvariant(), row.OneTime))
            .OrderBy(group => group.Min(row => row.StepOrder)))
        {
            var first = group.First();
            if (!group.Skip(1).Any())
            {
                merged.Add(first);
                continue;
            }

            List<string> steps = [];
            foreach (var step in group.OrderBy(row => row.StepOrder).SelectMany(row => row.ForSteps))
            {
                if (!steps.Contains(step, StringComparer.OrdinalIgnoreCase))
                {
                    steps.Add(step);
                }
            }

            merged.Add(new()
            {
                Step = first.Step,
                StepOrder = group.Min(row => row.StepOrder),
                Material = first.Material,
                DisplayMaterial = first.DisplayMaterial,
                Need = (uint)group.Sum(row => row.Need),
                OwnedInventory = first.OwnedInventory,
                OwnedQuestCredit = (uint)group.Sum(row => row.OwnedQuestCredit),
                Resolved = first.Resolved,
                Purchase = group.Select(row => row.Purchase).FirstOrDefault(purchase => purchase is not null),
                Obtain = first.Obtain,
                ObtainVia = first.ObtainVia,
                BuyAlternative = first.BuyAlternative,
                BuyAlternativeQuest = first.BuyAlternativeQuest,
                OneTime = first.OneTime,
                Craftable = first.Craftable,
                ForSteps = steps,
            });
        }

        return merged;
    }

    private void DrawArtisanWeaponListButtons(string expansionId, IReadOnlyList<ShoppingMaterialRow> rows)
    {
        if (ExpansionNames.IsTools(expansionId) || !ArtisanIpc.SupportsRelicToolLists)
        {
            return;
        }

        List<string> steps =
        [
            .. rows
                .Where(row => (row.Obtain == "craft" || row.Craftable) && row.Short > 0)
                .SelectMany(row => row.ForSteps)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(step => ArtisanIpc.TryGetRelicToolListId(step, ArtisanIpc.AllClassesSlot, out _))
        ];
        if (steps.Count == 0)
        {
            return;
        }

        foreach (var step in steps)
        {
            using (ImRaii.Disabled(ArtisanIpc.IsBusy()))
            {
                if (ImGui.SmallButton($"Craft {step} with Artisan##artisan_{step}")
                    && !ArtisanIpc.TryStartRelicToolList(step, ArtisanIpc.AllClassesSlot, out var error)
                    && !string.IsNullOrWhiteSpace(error))
                {
                    Svc.Log.Warning("[RelicTracker] {Error}", error);
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(
                    (ArtisanIpc.IsBusy() ? "Artisan is already crafting.\n\n" : string.Empty)
                    + $"Starts Artisan's premade {step} list - one weapon's worth, precrafts included.\n"
                    + "Vendor mats are not included - buy those first.");
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();
    }

    private void DrawGatherChillButton(IReadOnlyList<ShoppingMaterialRow> rows)
    {
        if (!GatherChillIpc.IsReady)
        {
            return;
        }

        List<(uint ItemId, uint Amount, string Name)> items = [];
        foreach (var row in rows)
        {
            var itemId = PrimaryItemId(row);
            if (row.Resolved && row.Short > 0 && itemId != 0 && GatherChillIpc.CanGather(itemId))
            {
                items.Add((itemId, row.Short, row.DisplayMaterial));
            }
        }

        if (items.Count > 0)
        {
            if (ImGui.SmallButton($"Add {items.Count} to Gather & Chill##gnc_add"))
            {
                gatherChillResult = GatherChillIpc.TryAddToGatherList(
                    [.. items.Select(item => (item.ItemId, item.Amount))],
                    out var added,
                    out var error)
                    ? new($"Added {Plural(added, "item")} to Gather & Chill's gather list.", false)
                    : new(error ?? "Gather & Chill didn't accept the list.", true);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Adds what's still short to Gather & Chill's gather list (items already listed keep the larger amount):\n"
                    + string.Join("\n", items.Select(item => $"  {item.Amount:N0} × {item.Name}")));
            }
        }
        else
        {
            ImGui.TextColored(MutedColor, "Nothing short here has a Gather & Chill route.");
        }

        if (GatherChillIpc.SupportsGatherItem && GatherChillIpc.IsBusy())
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Stop Gather & Chill##gnc_stop") && !GatherChillIpc.TryStop(out var error))
            {
                gatherChillResult = new(error ?? "Gather & Chill didn't stop.", true);
            }
        }

        if (gatherChillResult is not null)
        {
            ImGui.SameLine();
            DrawStatusMessage(ref gatherChillResult, "gnc_dismiss");
        }
    }

    private void DrawGatherChillRowButton(ShoppingMaterialRow row)
    {
        var itemId = PrimaryItemId(row);
        if (row.Short == 0 || itemId == 0 || !GatherChillIpc.CanGather(itemId))
        {
            return;
        }

        var direct = GatherChillIpc.SupportsGatherItem;
        var busy = direct && GatherChillIpc.IsBusy();
        var what = $"{row.Short:N0} × {row.DisplayMaterial}";
        ImGui.SameLine();
        using (ImRaii.Disabled(busy))
        {
            if (ImGui.SmallButton($"{(direct ? "Gather now" : "Add to list")}##gnc_row_{itemId}"))
            {
                var ok = direct
                    ? GatherChillIpc.TryGatherItem(itemId, row.Short, out var error)
                    : GatherChillIpc.TryAddToGatherList([(itemId, row.Short)], out _, out error);
                gatherChillResult = ok
                    ? new(direct ? $"Gather & Chill is gathering {what}." : $"Added {what} to Gather & Chill's gather list.", false)
                    : new(error ?? "Gather & Chill didn't accept the item.", true);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                busy ? "Gather & Chill is already gathering."
                : direct ? $"Gathers {what} with Gather & Chill right now, then stops."
                : $"Adds {what} to Gather & Chill's gather list.\nUpdate Gather & Chill to gather it straight away.");
        }
    }

    private void DrawLucisFirstJobPicker(string expansionId, string? lineFilter)
    {
        if (!ExpansionNames.IsTools(expansionId)
            || (!string.IsNullOrEmpty(lineFilter) && lineFilter != LucisToolShortcut.CollectType)
            || catalog.LinesFor(expansionId).FirstOrDefault(LucisToolShortcut.Applies) is not { } line)
        {
            return;
        }

        var ownership = GetOwnership();
        var jobs = line.EffectiveJobList;
        var drewLabel = false;
        foreach (var category in LucisToolShortcut.CategoryNames)
        {
            var unfinished = LucisToolShortcut.UnfinishedSlots(line, ownership, category);
            if (unfinished.Count < 2 || LucisToolShortcut.IsCategoryUnlocked(line, ownership, category))
            {
                continue;
            }

            if (!drewLabel)
            {
                // Same line as the summary sentence.
                ImGui.SameLine(0, 24);
                ImGui.TextColored(HeaderColor, "First Lucis tool");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Only this job's Supra and Lucis materials are counted"
                        + (GatherChillIpc.IsInstalled ? " (and sent to Knightshopper / Gather & Chill).\n" : " (and sent to Knightshopper).\n")
                        + $"Once it has its Lucis tool, a Calamity Salvager sells the others for {LucisToolShortcut.SalvagerGil:N0} gil each.");
                }

                drewLabel = true;
            }

            var first = LucisToolShortcut.FirstSlot(line, ownership, category, config.LucisFirstJob.GetValueOrDefault(category));
            ImGui.SameLine();
            ImGui.TextUnformatted(category == "crafter" ? "Crafter" : "Gatherer");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            if (ImGui.BeginCombo($"##lucis_first_{category}", first >= 0 && first < jobs.Count ? jobs[first] : "-"))
            {
                foreach (var slot in unfinished.Where(slot => slot < jobs.Count))
                {
                    if (ImGui.Selectable(jobs[slot], slot == first))
                    {
                        config.LucisFirstJob[category] = jobs[slot];
                        config.OnSettingChanged();
                        InvalidateShoppingCache();
                    }
                }

                ImGui.EndCombo();
            }
        }
    }

    private List<ShoppingQuestRewardRow> GetPrefarmRewards(string expansionId, IReadOnlyList<RelicLineStatus> statuses, Func<uint, uint> ownedLookup)
    {
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            return [];
        }

        var questRewards = ShoppingListBuilder.BuildQuestRewardIndex(sheet);
        var ownership = GetOwnership();
        var isTools = ExpansionNames.IsTools(expansionId);
        List<ShoppingQuestRewardRow> rewards = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (status, tiers, skipped) in UnfinishedSteps(statuses, ownership))
        {
            foreach (var tier in tiers)
            {
                foreach (var reward in ShoppingListBuilder.GetQuestRewards(status.Line.StepName(tier), questRewards, ownedLookup))
                {
                    // On tool lines a reward belongs to specific jobs (a Talan seal per gatherer); keep it
                    // only while one of those jobs still needs the step and isn't buying its Lucis tool.
                    if (isTools
                        && questRewards.ProductJobs.TryGetValue(reward.Material, out var jobs)
                        && !Enumerable.Range(0, status.Line.Jobs).Any(slot =>
                            ShoppingListBuilder.ToolMaterialAppliesToSlot(jobs, slot, stepHasFisherSection: true)
                            && !skipped.Contains(slot)
                            && !ownership.IsStepDoneOrManual(status.Line, slot, tier)))
                    {
                        continue;
                    }

                    if (seen.Add(reward.Material))
                    {
                        rewards.Add(reward);
                    }
                }
            }
        }

        return rewards;
    }

    private List<TrackerActivity> GetActivities(string expansionId, IReadOnlyList<RelicLineStatus> statuses)
    {
        List<TrackerActivity> activities = [];
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            return activities;
        }

        HashSet<string> stepsWithMaterials = new(
            sheet.Materials
                .Where(row => !ShoppingListBuilder.IsQuestRewardRow(row) && row.PerUnitCount > 0)
                .Select(row => row.Step?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        var ownership = GetOwnership();
        foreach (var (status, tiers, skipped) in UnfinishedSteps(statuses, ownership))
        {
            foreach (var tier in tiers)
            {
                var step = status.Line.StepName(tier);
                if (stepsWithMaterials.Contains(step)
                    || (!string.IsNullOrWhiteSpace(materialFilter)
                        && !step.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                        && !status.Line.CollectType.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var jobsLeft = Enumerable.Range(0, status.Line.Jobs)
                    .Count(slot => !skipped.Contains(slot) && !ownership.IsStepDoneOrManual(status.Line, slot, tier));
                if (jobsLeft == 0)
                {
                    continue;
                }

                var note = catalog.StepNote(status.Line.CollectType, step);
                activities.Add(new(
                    status.Line.CollectType,
                    step,
                    jobsLeft,
                    note?.Replace("[[Crafters]]", "\nCrafters:\n")
                        .Replace("[[Gatherers]]", "\nGatherers:\n")
                        .Replace("[[Fisher]]", "\nFisher:\n")));
            }
        }

        return activities;
    }

    private IEnumerable<(RelicLineStatus Status, List<int> Tiers, HashSet<int> Skipped)> UnfinishedSteps(
        IReadOnlyList<RelicLineStatus> statuses,
        RelicOwnership ownership)
    {
        foreach (var status in statuses.OrderBy(status => status.Line.TypeOrder))
        {
            List<int> tiers = [.. Enumerable.Range(0, status.TierCount).Where(tier => status.Line.Jobs - status.ReachedPerStep[tier] > 0)];
            if (tiers.Count > 0)
            {
                yield return (status, tiers, LucisToolShortcut.SkippedSlots(status.Line, ownership, config.LucisFirstJob));
            }
        }
    }
}
