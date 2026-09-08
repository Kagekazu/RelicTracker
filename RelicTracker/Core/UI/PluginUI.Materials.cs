using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private const ImGuiTableFlags ShoppingTableFlags =
        ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg;

    private string? cachedShoppingExpansionId;
    private string? cachedShoppingLineFilter;
    private int cachedShoppingGeneration;
    private long cachedShoppingOwnedStamp;
    private List<ShoppingMaterialRow>? cachedShoppingMaterials;

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
                lineFilter)
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

        if (config.HideCompleteMaterials)
        {
            materials = [.. materials.Where(row => row.Short > 0)];
        }

        data.ArmorCosts.TryGetValue(expansionId, out var armorCosts);

        if (BeginPanel("tracker_summary"))
        {
            DrawShoppingSummary(materials, armorCosts, expansionId, ownedLookup);
            EndPanel();
        }

        var hasArmor = armorCosts is { Count: > 0 };
        var drewAny = false;

        if (materials.Count > 0)
        {
            drewAny = true;
            DrawWeaponsList(expansionId, materials, ownedLookup);
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
            var badge = shortCount > 0 ? $"{rows.Count} items · {shortCount} short" : $"{rows.Count} items";
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

    private void DrawMaterialRow(ShoppingMaterialRow row)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        if (row.Resolved)
        {
            ImGui.TextUnformatted(row.DisplayMaterial);
            DrawPurchaseTooltip(row);
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
            ImGui.TextColored(MutedColor, "—");
        }

        ImGui.TableNextColumn();
        if (row.Resolved)
        {
            ImGui.TextColored(row.Short == 0 ? GoodColor : BadColor, row.Short.ToString());
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
            return null;
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
        var badge = ownedCount > 0 ? $"{ownedCount}/{rewards.Count} in inventory" : $"{rewards.Count} rewards";
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
        if (!DrawCollapsingSection($"{expansionId}|Armours", "Armours — currency per stage", true))
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
            ImGui.TextUnformatted(cost.AllTotal > 0 ? cost.AllTotal.ToString() : "—");

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
                ImGui.TextColored(MutedColor, "—");
            }

            ImGui.TableNextColumn();
            if (resolved && cost.AllTotal > 0)
            {
                var shortfall = (uint)cost.AllTotal > owned ? (uint)cost.AllTotal - owned : 0;
                ImGui.TextColored(shortfall == 0 ? GoodColor : BadColor, shortfall.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "—");
            }

            if (!rowHovered)
            {
                continue;
            }

            var perPiece = cost.PerPiece > 0 ? cost.PerPiece.ToString() : "varies";
            var detail = $"Per piece: {perPiece} {currencyLabel}\nPer set: {(cost.SetTotal > 0 ? cost.SetTotal.ToString() : "—")}";
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
            ImGui.TextColored(BadColor,
                $"{weaponShort} weapon material{(weaponShort == 1 ? string.Empty : "s")} and {armorShort} armor currenc{(armorShort == 1 ? "y" : "ies")} still short.");
        }
        else if (armorShort > 0)
        {
            ImGui.TextColored(BadColor,
                $"{armorShort} armor currenc{(armorShort == 1 ? "y" : "ies")} still short.");
        }
        else
        {
            ImGui.TextColored(BadColor, $"{weaponShort} material{(weaponShort == 1 ? string.Empty : "s")} still short.");
        }

        if (unresolved > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, $"({unresolved} items couldn't be matched — Owned won't count)");
        }

        ImGui.TextColored(MutedColor,
            "Weapon needs cover every job still missing the step. Armor owned includes currency in inventory plus finished pieces already bought.");
    }

    private static void DrawNeedOwnedShortHeaders(bool includeItem)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        if (includeItem)
        {
            ImGui.TableNextColumn();
            ImGui.TableHeader("Item");
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
