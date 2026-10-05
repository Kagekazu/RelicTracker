using RelicTracker.IPC;

namespace RelicTracker;

/// <summary>
///     Tracker "what to do" view: materials grouped by how you get them (buy / craft / gather &amp;
///     fish / content), one-time steps, prefarmable quest rewards, and steps that are pure
///     activities with nothing to stockpile.
/// </summary>
public sealed partial class PluginUI
{
    private readonly record struct TrackerActivity(string Line, string Step, int JobsLeft, string? Note);

    private StatusMessage? gatherChillResult;

    /// <summary>
    ///     One row per material: a material needed by several steps or zones is summed, and the
    ///     inventory is counted once instead of once per row.
    /// </summary>
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
                OneTime = first.OneTime,
                Craftable = first.Craftable,
                ForSteps = steps,
            });
        }

        return merged;
    }

    private void DrawObtainGroups(
        string expansionId,
        IReadOnlyList<ShoppingMaterialRow> materials,
        IReadOnlyList<RelicLineStatus> statuses,
        Func<uint, uint> ownedLookup)
    {
        List<ShoppingMaterialRow> oneTime = [.. materials.Where(row => row.OneTime)];
        List<ShoppingMaterialRow> perWeapon = [.. materials.Where(row => !row.OneTime)];

        DrawObtainSection(
            expansionId,
            "OneTime",
            "One-time steps",
            "Done once, before your first weapon of that step - not again for every job.",
            oneTime,
            row => row.ObtainLabel,
            () => DrawArtisanWeaponListButtons(expansionId, oneTime));

        DrawBuySection(expansionId, [.. perWeapon.Where(row => row.Obtain == "buy")]);

        List<ShoppingMaterialRow> craftRows = [.. perWeapon.Where(row => row.Obtain == "craft")];
        DrawObtainSection(
            expansionId,
            "Craft",
            "Craft",
            expansionId == ExpansionNames.ToolsId && ArtisanIpc.SupportsRelicToolLists
                ? "Crafter tool steps have an Artisan list on the Relic tab."
                : null,
            craftRows,
            null,
            () => DrawArtisanWeaponListButtons(expansionId, craftRows));

        List<ShoppingMaterialRow> gatherRows = [.. perWeapon.Where(row => row.Obtain is "gather" or "fish")];
        DrawObtainSection(
            expansionId,
            "Gather",
            "Gather & fish",
            null,
            gatherRows,
            // Only split Gather / Diadem / Fish when there's more than one of them.
            gatherRows.Select(row => row.ObtainLabel).Distinct().Skip(1).Any() ? row => row.ObtainLabel : null,
            () => DrawGatherChillButton(gatherRows));

        DrawObtainSection(
            expansionId,
            "Content",
            "Farm in content",
            "Drops and trades from duties, FATEs, and field zones.",
            [.. perWeapon.Where(row => row.Obtain is "content" or null)],
            row => data.MaterialSources.TryGetValue(row.Material, out var zone) ? zone : row.Step);

        DrawPrefarmRewards(expansionId, statuses, ownedLookup);
    }

    private void DrawBuySection(string expansionId, List<ShoppingMaterialRow> rows)
    {
        if (rows.Count == 0 || !DrawObtainHeader(expansionId, "Buy", "Buy", rows))
        {
            return;
        }

        DrawArtisanWeaponListButtons(expansionId, rows);
        var shopping = DrawKnightshopperStatus();
        List<ObtainGroup> groups = [];
        foreach (var group in rows
            .GroupBy(row => row.ObtainVia ?? "Other")
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<ShoppingMaterialRow> groupRows = [.. group];
            List<KnightshopperPurchase> purchases = [];
            KnightshopperCurrency currency = default;
            if (!shopping
                && KnightshopperIpc.IsReady
                && KnightshopperIpc.TryParseCurrency(group.Key, out currency))
            {
                foreach (var row in groupRows)
                {
                    if (TryGetKnightshopperPurchase(row, out _, out var purchase))
                    {
                        purchases.Add(purchase);
                    }
                }
            }

            groups.Add(new(
                group.Key,
                groupRows,
                purchases.Count > 0
                    ? () => DrawKnightshopperBuyButton(currency, purchases, $"Buy {purchases.Count} with Knightshopper")
                    : null));
        }

        DrawObtainTable($"{expansionId}|Buy", groups);
    }

    private void DrawObtainSection(
        string expansionId,
        string key,
        string title,
        string? blurb,
        List<ShoppingMaterialRow> rows,
        Func<ShoppingMaterialRow, string>? subgroup,
        Action? actions = null)
    {
        if (rows.Count == 0 || !DrawObtainHeader(expansionId, key, title, rows))
        {
            return;
        }

        if (blurb is not null)
        {
            ImGui.TextColored(MutedColor, blurb);
        }

        actions?.Invoke();

        if (subgroup is null)
        {
            DrawObtainTable($"{expansionId}|{key}", [new(null, rows)]);
            return;
        }

        List<IGrouping<string, ShoppingMaterialRow>> groups =
        [
            .. rows.GroupBy(subgroup).OrderBy(group => group.Min(row => row.StepOrder))
        ];
        DrawObtainTable(
            $"{expansionId}|{key}",
            [
                .. groups.Select(group => new ObtainGroup(
                    groups.Count > 1 || !string.IsNullOrEmpty(group.Key)
                        ? string.IsNullOrEmpty(group.Key) ? "Other" : group.Key
                        : null,
                    [.. group]))
            ]);
    }

    /// <summary>
    ///     Weapon lines: one "Craft {step} with Artisan" button per step whose crafts still need making.
    ///     Each starts Artisan's premade list for one weapon's worth. Tool lines use the Relic tab.
    /// </summary>
    private void DrawArtisanWeaponListButtons(string expansionId, IReadOnlyList<ShoppingMaterialRow> rows)
    {
        if (expansionId == ExpansionNames.ToolsId || !ArtisanIpc.SupportsRelicToolLists)
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

    /// <summary>Adds what's still short (and has a Gather &amp; Chill route) to Gather &amp; Chill's gather list.</summary>
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

        if (gatherChillResult is not null)
        {
            ImGui.SameLine();
            DrawStatusMessage(ref gatherChillResult, "gnc_dismiss");
        }
    }

    /// <summary>
    ///     Lucis Tools: pick which job per category makes the first Lucis tool. Only that job's
    ///     materials are counted; the others buy theirs from a Calamity Salvager afterwards.
    /// </summary>
    private void DrawLucisFirstJobPicker(string expansionId, string? lineFilter)
    {
        if (expansionId != ExpansionNames.ToolsId
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

    private bool DrawObtainHeader(string expansionId, string key, string title, IReadOnlyList<ShoppingMaterialRow> rows)
    {
        var shortCount = rows.Count(row => row.Short > 0);
        var badge = shortCount > 0 ? $"{Plural(rows.Count, "item")} · {shortCount} short" : Plural(rows.Count, "item");
        var configKey = $"{expansionId}|Do|{key}";
        return DrawCollapsingSection(configKey, $"{title}  ({badge})###{configKey}", true);
    }

    /// <summary>A sub-group of a section's table: an optional divider row (with an optional button) over its rows.</summary>
    private sealed record ObtainGroup(string? Heading, IReadOnlyList<ShoppingMaterialRow> Rows, Action? HeadingAction = null);

    /// <summary>One table per section; sub-groups (currency, zone, ...) are divider rows, so the header shows once.</summary>
    private void DrawObtainTable(string id, IReadOnlyList<ObtainGroup> groups)
    {
        using var table = ImRaii.Table($"Obtain_{id}", 5, ShoppingTableFlags, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("For", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
        DrawNeedOwnedShortHeaders(includeItem: true, includeFor: true);

        foreach (var group in groups)
        {
            if (group.Heading is not null)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(HeaderColor, group.Heading);
                if (group.HeadingAction is not null)
                {
                    ImGui.SameLine();
                    group.HeadingAction();
                }
            }

            foreach (var row in group.Rows)
            {
                DrawMaterialRow(row, showFor: true);
            }
        }
    }

    private void DrawPrefarmRewards(string expansionId, IReadOnlyList<RelicLineStatus> statuses, Func<uint, uint> ownedLookup)
    {
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            return;
        }

        var questRewards = ShoppingListBuilder.BuildQuestRewardIndex(sheet);
        var ownership = GetOwnership();
        var isTools = expansionId == ExpansionNames.ToolsId;
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

        if (rewards.Count > 0)
        {
            DrawQuestRewardsSubsection($"{expansionId}|Do|Rewards", rewards);
        }
    }

    private void DrawActivities(string expansionId, IReadOnlyList<TrackerActivity> activities)
    {
        var configKey = $"{expansionId}|Do|Activities";
        if (!DrawCollapsingSection(configKey, $"Activities  ({Plural(activities.Count, "step")})###{configKey}", false))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Steps with nothing to stockpile - quests, duties, books, or light. Hover a step for what to do.");

        // The line column only says something when the steps come from more than one relic line.
        var showLine = activities.Select(activity => activity.Line).Distinct().Skip(1).Any();
        using var table = ImRaii.Table($"Activities_{expansionId}", showLine ? 3 : 2, ShoppingTableFlags, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        if (showLine)
        {
            ImGui.TableSetupColumn("Relic line", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        }

        ImGui.TableSetupColumn("Jobs left", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableHeadersRow();

        foreach (var activity in activities)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(activity.Step);
            if (!string.IsNullOrWhiteSpace(activity.Note) && ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                DrawNote(activity.Note, ImGui.GetFontSize() * 32f);
            }

            if (showLine)
            {
                ImGui.TableNextColumn();
                ImGui.TextColored(MutedColor, activity.Line);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(activity.JobsLeft.ToString());
        }
    }

    /// <summary>
    ///     Unfinished steps that have no material rows at all - the work is an activity. Matches the
    ///     material filter against the step and line names.
    /// </summary>
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

    /// <summary>
    ///     Each line's unfinished steps, plus the job slots to skip on it (Lucis Tools jobs that will
    ///     buy their tool from a Calamity Salvager).
    /// </summary>
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
