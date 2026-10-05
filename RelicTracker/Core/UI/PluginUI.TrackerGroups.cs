using RelicTracker.IPC;

namespace RelicTracker;

/// <summary>
///     Tracker "what to do" view: materials grouped by how you get them (buy / craft / gather &amp;
///     fish / content), one-time unlocks, prefarmable quest rewards, and steps that are pure
///     activities with nothing to stockpile.
/// </summary>
public sealed partial class PluginUI
{
    private readonly record struct TrackerActivity(string Line, string Step, int JobsLeft, string? Note);

    private string? gatherChillMessage;
    private bool gatherChillMessageIsError;

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
            "One-time unlocks",
            "Needed once per account before the first weapon of a step - not per job.",
            oneTime,
            row => row.ObtainLabel);

        DrawBuySection(expansionId, [.. perWeapon.Where(row => row.Obtain == "buy")]);

        DrawObtainSection(
            expansionId,
            "Craft",
            "Craft",
            expansionId == "DoHDoL" && ArtisanIpc.SupportsRelicToolLists
                ? "Crafter tool steps have an Artisan list on the Relic tab."
                : null,
            [.. perWeapon.Where(row => row.Obtain == "craft")],
            null);

        List<ShoppingMaterialRow> gatherRows = [.. perWeapon.Where(row => row.Obtain is "gather" or "fish")];
        DrawObtainSection(
            expansionId,
            "Gather",
            "Gather & fish",
            null,
            gatherRows,
            row => row.ObtainLabel,
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

        var shopping = DrawKnightshopperStatus();
        foreach (var group in rows
            .GroupBy(row => row.ObtainVia ?? "Other")
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<ShoppingMaterialRow> groupRows = [.. group];
            ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(HeaderColor, group.Key);

            if (!shopping
                && KnightshopperIpc.IsReady
                && KnightshopperIpc.TryParseCurrency(group.Key, out var currency))
            {
                List<KnightshopperPurchase> purchases = [];
                foreach (var row in groupRows)
                {
                    if (TryGetKnightshopperPurchase(row, out _, out var purchase))
                    {
                        purchases.Add(purchase);
                    }
                }

                if (purchases.Count > 0)
                {
                    ImGui.SameLine();
                    DrawKnightshopperBuyButton(currency, purchases, $"Buy {purchases.Count} with Knightshopper");
                }
            }

            DrawObtainTable($"{expansionId}|Buy|{group.Key}", groupRows);
        }
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
            DrawObtainTable($"{expansionId}|{key}", rows);
            return;
        }

        List<IGrouping<string, ShoppingMaterialRow>> groups =
        [
            .. rows.GroupBy(subgroup).OrderBy(group => group.Min(row => row.StepOrder))
        ];
        foreach (var group in groups)
        {
            if (groups.Count > 1 || !string.IsNullOrEmpty(group.Key))
            {
                ImGui.Spacing();
                ImGui.TextColored(HeaderColor, string.IsNullOrEmpty(group.Key) ? "Other" : group.Key);
            }

            DrawObtainTable($"{expansionId}|{key}|{group.Key}", [.. group]);
        }
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
            if (row.Resolved
                && row.Short > 0
                && data.MaterialIdsByName.TryGetValue(row.Material, out var ids)
                && ids.Count > 0
                && GatherChillIpc.CanGather(ids[0]))
            {
                items.Add((ids[0], row.Short, row.DisplayMaterial));
            }
        }

        if (items.Count > 0)
        {
            if (ImGui.SmallButton($"Add {items.Count} to Gather & Chill##gnc_add"))
            {
                gatherChillMessageIsError = !GatherChillIpc.TryAddToGatherList(
                    [.. items.Select(item => (item.ItemId, item.Amount))],
                    out var added,
                    out var error);
                gatherChillMessage = gatherChillMessageIsError
                    ? error
                    : $"Added {added} item{(added == 1 ? string.Empty : "s")} to Gather & Chill's gather list.";
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

        if (gatherChillMessage is { } message)
        {
            ImGui.SameLine();
            ImGui.TextColored(gatherChillMessageIsError ? BadColor : GoodColor, message);
        }
    }

    /// <summary>
    ///     Lucis Tools: pick which job per category makes the first Lucis tool. Only that job's
    ///     materials are counted; the others buy theirs from a Calamity Salvager afterwards.
    /// </summary>
    private void DrawLucisFirstJobPicker(string expansionId, string? lineFilter)
    {
        if (expansionId != "DoHDoL"
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
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(HeaderColor, "First Lucis tool");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Only this job's Supra and Lucis materials are counted (and sent to Knightshopper / Gather & Chill).\n"
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
        var badge = shortCount > 0 ? $"{rows.Count} items · {shortCount} short" : $"{rows.Count} items";
        var configKey = $"{expansionId}|Do|{key}";
        return DrawCollapsingSection(configKey, $"{title}  ({badge})###{configKey}", true);
    }

    private void DrawObtainTable(string id, IReadOnlyList<ShoppingMaterialRow> rows)
    {
        using var table = ImRaii.Table($"Obtain_{id}", 5, ShoppingTableFlags, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("For", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
        DrawNeedOwnedShortHeaders(includeItem: true, includeFor: true);

        foreach (var row in rows)
        {
            DrawMaterialRow(row, showFor: true);
        }
    }

    private void DrawPrefarmRewards(string expansionId, IReadOnlyList<RelicLineStatus> statuses, Func<uint, uint> ownedLookup)
    {
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            return;
        }

        var questRewards = ShoppingListBuilder.BuildQuestRewardIndex(sheet);
        List<ShoppingQuestRewardRow> rewards = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (status, tier) in UnfinishedSteps(statuses))
        {
            foreach (var reward in ShoppingListBuilder.GetQuestRewards(status.Line.StepName(tier), questRewards, ownedLookup))
            {
                if (seen.Add(reward.Material))
                {
                    rewards.Add(reward);
                }
            }
        }

        if (rewards.Count > 0)
        {
            DrawQuestRewardsSubsection($"{expansionId}|Do|Rewards", rewards);
        }
    }

    private void DrawActivities(string expansionId, IReadOnlyList<RelicLineStatus> statuses)
    {
        List<TrackerActivity> activities = [.. GetActivities(expansionId, statuses)];
        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            activities =
            [
                .. activities.Where(activity => activity.Step.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                                                || activity.Line.Contains(materialFilter, StringComparison.OrdinalIgnoreCase))
            ];
        }

        if (activities.Count == 0)
        {
            return;
        }

        var configKey = $"{expansionId}|Do|Activities";
        if (!DrawCollapsingSection(configKey, $"Activities  ({activities.Count} steps)###{configKey}", true))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Steps with nothing to stockpile - quests, duties, books, or light. Hover a step for what to do.");

        using var table = ImRaii.Table($"Activities_{expansionId}", 3, ShoppingTableFlags, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Step", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Relic line", ImGuiTableColumnFlags.WidthStretch, 0.5f);
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
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
                ImGui.TextUnformatted(activity.Note);
                ImGui.PopTextWrapPos();
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(MutedColor, activity.Line);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(activity.JobsLeft.ToString());
        }
    }

    /// <summary>Unfinished steps that have no material rows at all - the work is an activity.</summary>
    private IEnumerable<TrackerActivity> GetActivities(string expansionId, IReadOnlyList<RelicLineStatus> statuses)
    {
        if (!data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            yield break;
        }

        HashSet<string> stepsWithMaterials = new(
            sheet.Materials
                .Where(row => !ShoppingListBuilder.IsQuestRewardRow(row) && row.PerUnitCount > 0)
                .Select(row => row.Step?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        var ownership = GetOwnership();
        foreach (var (status, tier) in UnfinishedSteps(statuses))
        {
            var step = status.Line.StepName(tier);
            if (stepsWithMaterials.Contains(step))
            {
                continue;
            }

            // Jobs that will buy their Lucis tool from a Calamity Salvager skip every earlier step.
            var skipped = LucisToolShortcut.SkippedSlots(status.Line, ownership, config.LucisFirstJob);
            var jobsLeft = Enumerable.Range(0, status.Line.Jobs)
                .Count(slot => !skipped.Contains(slot) && !ownership.IsStepDoneOrManual(status.Line, slot, tier));
            if (jobsLeft == 0)
            {
                continue;
            }

            var note = catalog.StepNote(status.Line.CollectType, step);
            yield return new(
                status.Line.CollectType,
                step,
                jobsLeft,
                note?.Replace("[[Crafters]]", "\n\nCrafters: ")
                    .Replace("[[Gatherers]]", "\n\nGatherers: ")
                    .Replace("[[Fisher]]", "\n\nFisher: "));
        }
    }

    private static IEnumerable<(RelicLineStatus Status, int Tier)> UnfinishedSteps(IReadOnlyList<RelicLineStatus> statuses)
    {
        foreach (var status in statuses.OrderBy(status => status.Line.TypeOrder))
        {
            for (var tier = 0; tier < status.TierCount; tier++)
            {
                if (status.Line.Jobs - status.ReachedPerStep[tier] > 0)
                {
                    yield return (status, tier);
                }
            }
        }
    }
}
