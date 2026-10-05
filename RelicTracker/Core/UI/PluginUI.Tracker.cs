namespace RelicTracker;

public sealed partial class PluginUI
{
    private const ImGuiTableFlags ShoppingTableFlags =
        ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg;

    private string materialFilter = string.Empty;
    private string? cachedShoppingExpansionId;
    private string? cachedShoppingLineFilter;
    private int cachedShoppingGeneration;
    private long cachedShoppingOwnedStamp;
    private List<ShoppingMaterialRow>? cachedShoppingMaterials;

    private void DrawTrackerTab()
    {
        trackerTabVisible = true;
        RefreshCollectIfStale();

        DrawTabIntro("Shopping list for unfinished jobs. Open Relic for per-job steps and notes.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Expansion");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##expansion-tracker", ExpansionNames.LongName(config.SelectedExpansionId)))
        {
            foreach (var expansionId in data.Manifest.Expansions)
            {
                if (ImGui.Selectable(ExpansionNames.LongName(expansionId), expansionId == config.SelectedExpansionId))
                {
                    config.SelectedExpansionId = expansionId;
                    config.TrackerLineFilter = string.Empty;
                    config.OnSettingChanged();
                }
            }

            ImGui.EndCombo();
        }

        List<RelicLine> lines = [.. catalog.LinesFor(config.SelectedExpansionId)];
        var multiLine = lines.Count > 1;
        if (!multiLine)
        {
            if (!string.IsNullOrEmpty(config.TrackerLineFilter))
            {
                config.TrackerLineFilter = string.Empty;
                config.OnSettingChanged();
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(config.TrackerLineFilter) && lines.All(l => l.CollectType != config.TrackerLineFilter))
            {
                config.TrackerLineFilter = string.Empty;
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Line");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(190);
            var focusLabel = string.IsNullOrEmpty(config.TrackerLineFilter) ? "All lines" : config.TrackerLineFilter;
            if (ImGui.BeginCombo("##line-tracker", focusLabel))
            {
                if (ImGui.Selectable("All lines", string.IsNullOrEmpty(config.TrackerLineFilter)))
                {
                    config.TrackerLineFilter = string.Empty;
                    config.OnSettingChanged();
                }

                foreach (var line in lines)
                {
                    if (ImGui.Selectable(line.CollectType, line.CollectType == config.TrackerLineFilter))
                    {
                        config.TrackerLineFilter = line.CollectType;
                        config.OnSettingChanged();
                    }
                }

                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();

        var hideComplete = config.HideCompleteMaterials;
        if (ImGui.Checkbox("Hide finished materials", ref hideComplete))
        {
            config.HideCompleteMaterials = hideComplete;
            config.OnSettingChanged();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##filter", "Filter materials…", ref materialFilter, 128);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Group by");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##groupby-tracker", config.TrackerGroupBySteps ? "Relic step" : "What to do"))
        {
            if (ImGui.Selectable("What to do", !config.TrackerGroupBySteps))
            {
                config.TrackerGroupBySteps = false;
                config.OnSettingChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Buy, craft, gather & fish, farm in content - with one-time unlocks and activities listed separately.");
            }

            if (ImGui.Selectable("Relic step", config.TrackerGroupBySteps))
            {
                config.TrackerGroupBySteps = true;
                config.OnSettingChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Each relic step (or farming zone) with its materials.");
            }

            ImGui.EndCombo();
        }

        EndStickyHeader();

        DrawShoppingList(config.SelectedExpansionId, ImGui.GetContentRegionAvail().Y);
    }

    /// <summary>Line statuses for the Tracker's expansion and line filter.</summary>
    private IReadOnlyList<RelicLineStatus> TrackerStatuses(string expansionId, string? lineFilter) =>
    [
        .. RelicStatusService.Build(GetOwnership(), catalog, config.HidePhyseosRelics)
            .Where(status => string.Equals(status.Line.Expansion, expansionId, StringComparison.Ordinal))
            .Where(status => string.IsNullOrEmpty(lineFilter)
                             || string.Equals(status.Line.CollectType, lineFilter, StringComparison.Ordinal))
    ];

    private void InvalidateShoppingCache()
    {
        cachedShoppingMaterials = null;
        cachedShoppingExpansionId = null;
        cachedShoppingLineFilter = null;
        cachedShoppingGeneration = 0;
        cachedShoppingOwnedStamp = 0;
    }

    private List<ShoppingMaterialRow> GetTrackerShoppingMaterials(
        string expansionId,
        string? lineFilter,
        Func<uint, uint> ownedLookup)
    {
        long ownedStamp = OwnedCountRefreshStamp();
        if (cachedShoppingMaterials is not null
            && cachedShoppingExpansionId == expansionId
            && cachedShoppingLineFilter == lineFilter
            && cachedShoppingGeneration == cacheGeneration
            && cachedShoppingOwnedStamp == ownedStamp)
        {
            return cachedShoppingMaterials;
        }

        RelicOwnership ownership = GetOwnership();
        IReadOnlyList<RelicLineStatus> statuses = RelicStatusService.Build(ownership, catalog, config.HidePhyseosRelics);
        List<ShoppingMaterialRow> materials = data.Expansions.TryGetValue(expansionId, out var sheet)
            ? ShoppingListBuilder.Build(
                expansionId,
                sheet,
                statuses,
                ownership,
                ownedLookup,
                data.MaterialSources,
                data.MaterialIdsByName,
                lineFilter,
                config.LucisFirstJob)
            : [];

        cachedShoppingMaterials = materials;
        cachedShoppingExpansionId = expansionId;
        cachedShoppingLineFilter = lineFilter;
        cachedShoppingGeneration = cacheGeneration;
        cachedShoppingOwnedStamp = ownedStamp;
        return materials;
    }

    private void DrawShoppingList(string expansionId, float regionHeight)
    {
        using var pane = ImRaii.Child("##TrackerMaterialsPane", new(0, regionHeight), false);
        if (!pane)
        {
            return;
        }

        DrawProgressSourceHint(ProgressHintContext.Tracker);

        string? lineFilter = string.IsNullOrEmpty(config.TrackerLineFilter) ? null : config.TrackerLineFilter;
        Func<uint, uint> ownedLookup = CreateOwnedLookup();
        List<ShoppingMaterialRow> materials = GetTrackerShoppingMaterials(expansionId, lineFilter, ownedLookup);

        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            materials =
            [
                .. materials
                    .Where(row => row.DisplayMaterial.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                                  || row.Material.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                                  || row.Step.Contains(materialFilter, StringComparison.OrdinalIgnoreCase))
            ];
        }

        var bySteps = config.TrackerGroupBySteps;
        if (!bySteps)
        {
            materials = MergeByMaterial(materials);
        }

        if (config.HideCompleteMaterials)
        {
            materials = [.. materials.Where(row => row.Short > 0)];
        }

        data.ArmorCosts.TryGetValue(expansionId, out var armorCosts);

        DrawShoppingSummary(materials, armorCosts, expansionId, ownedLookup);
        DrawLucisFirstJobPicker(expansionId, lineFilter);
        if (bySteps)
        {
            DrawKnightshopperBar(materials);
        }

        ImGui.Spacing();

        var hasArmor = armorCosts is { Count: > 0 };
        var drewAny = false;

        if (materials.Count > 0)
        {
            drewAny = true;
            if (bySteps)
            {
                DrawWeaponsList(expansionId, materials, ownedLookup);
            }
        }

        if (!bySteps)
        {
            IReadOnlyList<RelicLineStatus> statuses = TrackerStatuses(expansionId, lineFilter);
            if (materials.Count > 0)
            {
                DrawObtainGroups(expansionId, materials, statuses, ownedLookup);
            }

            List<TrackerActivity> activities = GetActivities(expansionId, statuses);
            if (activities.Count > 0)
            {
                drewAny = true;
                DrawActivities(expansionId, activities);
            }
        }

        if (hasArmor)
        {
            drewAny = true;
            DrawArmoursList(expansionId, armorCosts!, ownedLookup);
        }

        if (!drewAny)
        {
            if (BeginPanel("tracker_empty"))
            {
                ImGui.TextColored(GoodColor, config.HideCompleteMaterials
                    ? "Nothing left to farm for this expansion."
                    : "No tracked materials for this expansion.");
                EndPanel();
            }
        }
    }

    private void DrawWeaponsList(
        string expansionId,
        IReadOnlyList<ShoppingMaterialRow> materials,
        Func<uint, uint> ownedLookup)
    {
        if (!DrawCollapsingSection($"{expansionId}|Weapons", "Weapons & tools", true))
        {
            return;
        }

        ShoppingListBuilder.QuestRewardIndex? questRewards = null;
        if (data.Expansions.TryGetValue(expansionId, out var sheet))
        {
            questRewards = ShoppingListBuilder.BuildQuestRewardIndex(sheet);
        }

        foreach (var group in materials
            .GroupBy(row => row.Step)
            .OrderBy(g => g.Min(row => row.StepOrder)))
        {
            List<ShoppingMaterialRow> rows = [.. group.OrderBy(row => row.StepOrder)];
            var shortCount = rows.Count(row => row.Short > 0);
            var badge = shortCount > 0 ? $"{Plural(rows.Count, "item")} · {shortCount} short" : Plural(rows.Count, "item");
            var key = $"{expansionId}|W|{group.Key}";
            if (!DrawCollapsingSection(key, $"{group.Key}  ({badge})###{key}", false))
            {
                continue;
            }

            if (questRewards is not null)
            {
                DrawQuestRewardsSubsection(
                    $"{key}|Rewards",
                    ShoppingListBuilder.GetQuestRewards(group.Key, questRewards, ownedLookup));
            }

            using var table = ImRaii.Table($"WGrp_{key}", 4, ShoppingTableFlags, new(0, 0));
            if (!table)
            {
                continue;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 0.6f);
            ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
            DrawNeedOwnedShortHeaders(includeItem: true);

            foreach (var row in rows)
            {
                DrawMaterialRow(row);
            }
        }
    }

    private void DrawMaterialRow(ShoppingMaterialRow row, bool showFor = false)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        if (row.Resolved)
        {
            ImGui.TextUnformatted(row.DisplayMaterial);
            DrawPurchaseTooltip(row);
            if (row.OneTime)
            {
                ImGui.SameLine();
                ImGui.TextColored(MutedColor, "(one-time step)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("One-time step: needed once, not again for every job.");
                }
            }
        }
        else
        {
            ImGui.TextColored(WarningColor, row.DisplayMaterial);
            if (ImGui.IsItemHovered())
            {
                var tooltip = "Couldn't match this to a game item, so owned can't be counted.";
                if (PurchaseSummary(row) is { } cost)
                {
                    tooltip += $"\n\n{cost}";
                }

                ImGui.SetTooltip(tooltip);
            }
        }

        if (showFor)
        {
            ImGui.TableNextColumn();
            var steps = string.Join(", ", row.ForSteps);
            ImGui.TextColored(MutedColor, steps);
            if (row.ForSteps.Count > 1 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(string.Join("\n", row.ForSteps));
            }
        }

        ImGui.TableNextColumn();
        ImGui.Text(row.Need.ToString());

        ImGui.TableNextColumn();
        if (row.Resolved)
        {
            ImGui.Text(row.OwnedInventory.ToString());
            if (row.OwnedQuestCredit > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"+{row.OwnedQuestCredit} counted from prefarmed quest rewards in inventory.\n"
                    + $"Effective owned: {row.Owned} (inventory {row.OwnedInventory} + quest credit {row.OwnedQuestCredit}).");
            }
        }
        else
        {
            ImGui.TextColored(MutedColor, "-");
        }

        ImGui.TableNextColumn();
        if (row.Resolved)
        {
            ImGui.TextColored(row.Short == 0 ? GoodColor : ShortColor, row.Short.ToString());
        }
        else
        {
            ImGui.TextColored(MutedColor, "?");
        }
    }

    private static void DrawPurchaseTooltip(ShoppingMaterialRow row)
    {
        if (ImGui.IsItemHovered() && PurchaseSummary(row) is { } summary)
        {
            ImGui.SetTooltip(summary);
        }
    }

    private static string? PurchaseSummary(ShoppingMaterialRow row)
    {
        if (row.Purchase is not { Unit: > 0 } purchase)
        {
            return string.IsNullOrEmpty(row.ObtainLabel) ? null : row.ObtainLabel;
        }

        long unit = purchase.Unit;
        var currency = purchase.Currency;
        var summary = $"{unit:N0} {currency} each\nNeed {row.Need:N0} \u2192 {unit * row.Need:N0} {currency}";
        if (row.Short > 0)
        {
            summary += $"\nStill short {row.Short:N0} \u2192 {unit * row.Short:N0} {currency}";
        }

        return summary;
    }

    private void DrawQuestRewardsSubsection(string configKey, IReadOnlyList<ShoppingQuestRewardRow> rewards)
    {
        var ownedCount = rewards.Count(row => row.Owned > 0);
        var badge = ownedCount > 0 ? $"{ownedCount}/{rewards.Count} in inventory" : Plural(rewards.Count, "reward");
        DrawQuestRewardsSection(
            configKey,
            rewards,
            $"Prefarmed quest rewards  ({badge})###{configKey}",
            "Repeatable sub-quest turn-ins. Owning these credits their materials in the table below.",
            ShoppingTableFlags,
            ownedColumnWidth: 64,
            showResolvedTooltip: true);
    }

    private void DrawArmoursList(string expansionId, IReadOnlyList<ArmorCostRow> costs, Func<uint, uint> ownedLookup)
    {
        if (!DrawCollapsingSection($"{expansionId}|Armours", "Armours - currency per stage", true))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Need is for all unfinished sets. Hover a row for per-piece cost.");
        ImGui.Spacing();

        using var table = ImRaii.Table(
            $"ArmoursList_{expansionId}",
            5,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Stage", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 76);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 72);
        DrawNeedOwnedShortHeaders(includeItem: false);

        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Selectable($"{cost.Set}##armor_stage_{index}", false, ImGuiSelectableFlags.SpanAllColumns);
            var rowHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly);

            ImGui.TableNextColumn();
            var resolved = cost.CurrencyIds.Count > 0;
            var currencyLabel = ItemDisplayNames.Label(cost.CurrencyIds, cost.Currency);
            if (resolved)
            {
                ImGui.TextUnformatted(currencyLabel);
            }
            else
            {
                ImGui.TextColored(WarningColor, currencyLabel);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.AllTotal > 0 ? cost.AllTotal.ToString() : "-");

            (uint ownedInventory, uint ownedArmorCredit, uint owned) = resolved
                ? ArmorCurrencyOwned(expansionId, cost, ownedLookup)
                : (0u, 0u, 0u);

            ImGui.TableNextColumn();
            if (resolved)
            {
                ImGui.Text(ownedInventory.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "-");
            }

            ImGui.TableNextColumn();
            if (resolved && cost.AllTotal > 0)
            {
                var shortfall = (uint)cost.AllTotal > owned ? (uint)cost.AllTotal - owned : 0;
                ImGui.TextColored(shortfall == 0 ? GoodColor : ShortColor, shortfall.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "-");
            }

            if (!rowHovered)
            {
                continue;
            }

            var perPiece = cost.PerPiece > 0 ? cost.PerPiece.ToString() : "varies";
            var detail = $"Per piece: {perPiece} {currencyLabel}\nPer set: {(cost.SetTotal > 0 ? cost.SetTotal.ToString() : "-")}";
            if (!string.IsNullOrWhiteSpace(cost.Note))
            {
                detail += $"\n\n{cost.Note}";
            }

            if (ownedArmorCredit > 0)
            {
                detail +=
                    $"\n\n+{ownedArmorCredit} counted from owned armor pieces.\n"
                    + $"Effective owned: {owned} (inventory {ownedInventory} + armor {ownedArmorCredit}).";
            }

            ImGui.SetTooltip(detail);
        }
    }

    private (uint Inventory, uint ArmorCredit, uint Owned) ArmorCurrencyOwned(
        string expansionId,
        ArmorCostRow cost,
        Func<uint, uint> ownedLookup)
    {
        uint inventory = ShoppingListBuilder.SumOwned(cost.CurrencyIds, ownedLookup);
        uint armorCredit = ArmorCostCalculator.ArmorPieceCredit(expansionId, cost, catalog, ownedLookup);
        return (inventory, armorCredit, inventory + armorCredit);
    }

    private void DrawShoppingSummary(
        IReadOnlyList<ShoppingMaterialRow> materials,
        IReadOnlyList<ArmorCostRow>? armorCosts,
        string expansionId,
        Func<uint, uint> ownedLookup)
    {
        var weaponShort = materials.Count(row => row.Short > 0);
        var armorShort = 0;
        if (armorCosts is not null)
        {
            foreach (var cost in armorCosts)
            {
                if (cost.CurrencyIds.Count == 0 || cost.AllTotal <= 0)
                {
                    continue;
                }

                (_, _, uint owned) = ArmorCurrencyOwned(expansionId, cost, ownedLookup);
                if ((uint)cost.AllTotal > owned)
                {
                    armorShort++;
                }
            }
        }

        var shortCount = weaponShort + armorShort;
        var unresolved = materials.Count(row => !row.Resolved);

        if (shortCount == 0)
        {
            ImGui.TextColored(GoodColor, "You have enough of every tracked material and armor currency for your remaining sets.");
        }
        else if (weaponShort > 0 && armorShort > 0)
        {
            ImGui.TextColored(ShortColor,
                $"{Plural(weaponShort, "weapon material")} and {armorShort} armor currenc{(armorShort == 1 ? "y" : "ies")} still short.");
        }
        else if (armorShort > 0)
        {
            ImGui.TextColored(ShortColor,
                $"{armorShort} armor currenc{(armorShort == 1 ? "y" : "ies")} still short.");
        }
        else
        {
            ImGui.TextColored(ShortColor, $"{Plural(weaponShort, "material")} still short.");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Needs cover every job still missing the step."
                + (armorCosts is { Count: > 0 }
                    ? "\nArmor owned includes currency in inventory plus finished pieces already bought."
                    : string.Empty));
        }

        if (unresolved > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, $"({Plural(unresolved, "item")} couldn't be matched - Owned won't count)");
        }
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private static void DrawNeedOwnedShortHeaders(bool includeItem, bool includeFor = false)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        if (includeItem)
        {
            ImGui.TableNextColumn();
            ImGui.TableHeader("Item");
            if (includeFor)
            {
                ImGui.TableNextColumn();
                ImGui.TableHeader("For");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Relic steps that need this material.");
                }
            }
        }
        else
        {
            ImGui.TableNextColumn();
            ImGui.TableHeader("Stage");
            ImGui.TableNextColumn();
            ImGui.TableHeader("Currency");
        }

        ImGui.TableNextColumn();
        ImGui.TableHeader("Need");

        ImGui.TableNextColumn();
        ImGui.TableHeader("Owned");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("In bags, retainers, dresser, and armoire (Allagan Tools).");
        }

        ImGui.TableNextColumn();
        ImGui.TableHeader("Short");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Still to farm after counting inventory plus prefarmed quest rewards or owned armor pieces.");
        }
    }

    private bool DrawCollapsingSection(string configKey, string header, bool defaultOpen)
    {
        var isOpen = config.ExpandedMaterialSections.TryGetValue(configKey, out var saved) ? saved : defaultOpen;
        var nodeOpen = ImGui.CollapsingHeader(header, isOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (nodeOpen != isOpen)
        {
            config.ExpandedMaterialSections[configKey] = nodeOpen;
            config.OnSettingChanged();
        }
        else if (!config.ExpandedMaterialSections.ContainsKey(configKey))
        {
            config.ExpandedMaterialSections[configKey] = nodeOpen;
        }

        return nodeOpen;
    }
}
