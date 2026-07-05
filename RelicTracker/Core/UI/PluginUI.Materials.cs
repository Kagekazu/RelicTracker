using RelicTracker.IPC;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private const ImGuiTableFlags ShoppingTableFlags =
        ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg;
    private void DrawShoppingList(string expansionId, float regionHeight)
    {
        using var pane = ImRaii.Child("##TrackerMaterialsPane", new(0, regionHeight), false);
        if (!pane)
        {
            return;
        }

        DrawProgressSourceHint(ProgressHintContext.Tracker);

        string? lineFilter = string.IsNullOrEmpty(config.TrackerLineFilter) ? null : config.TrackerLineFilter;

        RelicOwnership ownership = GetOwnership();
        IReadOnlyList<RelicLineStatus> statuses = RelicStatusService.Build(ownership, catalog);
        Func<uint, uint> ownedLookup = CreateOwnedLookup();

        List<ShoppingMaterialRow> materials = data.GetShoppingMaterials(expansionId, statuses, ownership, itemResolver, ownedLookup, lineFilter);

        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            materials =
            [
                .. materials
                    .Where(row => row.Material.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                                  || row.Step.Contains(materialFilter, StringComparison.OrdinalIgnoreCase))
            ];
        }

        if (config.HideCompleteMaterials)
        {
            materials = [.. materials.Where(row => row.Short > 0)];
        }

        DrawShoppingSummary(materials);
        ImGui.Spacing();

        data.ArmorCosts.TryGetValue(expansionId, out var armorCosts);
        var hasArmor = armorCosts is { Count: > 0 };
        var drewAny = false;

        if (materials.Count > 0)
        {
            drewAny = true;
            DrawWeaponsList(expansionId, materials);
        }

        if (hasArmor)
        {
            drewAny = true;
            DrawArmoursList(expansionId, armorCosts!, ownedLookup);
        }

        if (!drewAny)
        {
            ImGui.TextColored(GoodColor, config.HideCompleteMaterials
                ? "Nothing left to farm for this expansion."
                : "No tracked materials for this expansion.");
        }
    }

    private void DrawWeaponsList(
        string expansionId,
        IReadOnlyList<ShoppingMaterialRow> materials)
    {
        if (!DrawCollapsingSection($"{expansionId}|Weapons", "Weapons & tools", true))
        {
            return;
        }

        // Group by where you get it (zone or step); each group is its own collapsible block so the
        // list stays scannable. The group header is the "where", so rows drop that column.
        foreach (var group in materials
            .GroupBy(row => WhereToGet(expansionId, row))
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

            using var table = ImRaii.Table($"WGrp_{key}", 4, ShoppingTableFlags, new(0, 0));
            if (!table)
            {
                continue;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 0.6f);
            ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableHeadersRow();

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
            ImGui.TextUnformatted(row.Material);
            DrawPurchaseTooltip(row);
        }
        else
        {
            ImGui.TextColored(WarningColor, row.Material);
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
            ImGui.Text(row.Owned.ToString());
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

    /// <summary>Vendor-price breakdown for a purchasable material (per unit, total need, total short).</summary>
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

    private void DrawArmoursList(string expansionId, IReadOnlyList<ArmorCostRow> costs, Func<uint, uint> ownedLookup)
    {
        if (!DrawCollapsingSection($"{expansionId}|Armours", "Armours — currency per stage", true))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Need = every set (all jobs/roles). Hover a stage for per-set / per-piece / slot detail. Short = Need − Owned.");
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
        ImGui.TableHeadersRow();

        foreach (var cost in costs)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.Set);
            if (ImGui.IsItemHovered())
            {
                var perPiece = cost.PerPiece > 0 ? cost.PerPiece.ToString() : "varies";
                var detail = $"Per piece: {perPiece}\nPer set: {(cost.SetTotal > 0 ? cost.SetTotal.ToString() : "—")}";
                if (!string.IsNullOrWhiteSpace(cost.Note))
                {
                    detail += $"\n\n{cost.Note}";
                }

                ImGui.SetTooltip(detail);
            }

            ImGui.TableNextColumn();
            var itemIds = itemResolver.ResolveItemIds(cost.Currency);
            if (itemIds.Count > 0)
            {
                ImGui.TextUnformatted(cost.Currency);
            }
            else
            {
                ImGui.TextColored(WarningColor, cost.Currency);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.AllTotal > 0 ? cost.AllTotal.ToString() : "—");

            var resolved = itemIds.Count > 0;
            var owned = resolved
                ? itemIds.Aggregate(0u, (total, itemId) => total + ownedLookup(itemId))
                : 0u;

            ImGui.TableNextColumn();
            if (resolved)
            {
                ImGui.Text(owned.ToString());
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
        }
    }

    /// <summary>Where/how to get a material: farm zone (material_sources.json), else the relic step.</summary>
    private string WhereToGet(string expansionId, ShoppingMaterialRow row) =>
        data.MaterialSources.TryGetValue(row.Material, out var source) ? source : row.Step;

    private void DrawShoppingSummary(IReadOnlyList<ShoppingMaterialRow> materials)
    {
        var shortCount = materials.Count(row => row.Short > 0);
        var unresolved = materials.Count(row => !row.Resolved);

        if (shortCount == 0)
        {
            ImGui.TextColored(GoodColor, "You have enough of every tracked material for your remaining weapons.");
        }
        else
        {
            ImGui.TextColored(BadColor, $"{shortCount} material{(shortCount == 1 ? string.Empty : "s")} still needed.");
        }

        if (unresolved > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, $"({unresolved} not linked to an item)");
        }

        ImGui.TextColored(MutedColor, "Needs cover every job still missing the weapon. Owned counts are your live inventory.");
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
