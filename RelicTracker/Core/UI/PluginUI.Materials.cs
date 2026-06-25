using System.Numerics;
using RelicTracker.Framework;
using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private void DrawShoppingList(string expansionId, float regionHeight)
    {
        using var pane = ImRaii.Child("##TrackerMaterialsPane", new Vector2(0, regionHeight), false);
        if (!pane)
        {
            return;
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.TextWrapped(
                "Set a FFXIV Collect ID on the Collect tab so the shopping list knows which weapons you still need.");
            return;
        }

        var statuses = RelicStatusService.Build(ffxivCollect.Snapshot, catalog);
        var ownership = GetOwnership();
        var ownedLookup = (Func<uint, uint>)(itemId => AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly));
        var materials = data.GetShoppingMaterials(expansionId, statuses, ownership, itemResolver, ownedLookup);
        var currencies = data.GetExpansionCurrencies(expansionId, itemResolver, ownedLookup, progressTracker).ToList();

        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            materials = materials
                .Where(row => row.Material.Contains(materialFilter, StringComparison.OrdinalIgnoreCase)
                              || row.Step.Contains(materialFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            currencies = currencies
                .Where(row => row.Name.Contains(materialFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (config.HideCompleteMaterials)
        {
            materials = materials.Where(row => row.Short > 0).ToList();
            currencies = currencies.Where(row => row.Shortfall > 0).ToList();
        }

        DrawShoppingSummary(materials);
        ImGui.Spacing();

        var hasArmor = data.ArmorCosts.TryGetValue(expansionId, out var armorCosts) && armorCosts.Count > 0;
        var drewAny = false;

        if (materials.Count > 0 || currencies.Count > 0)
        {
            drewAny = true;
            DrawWeaponsList(expansionId, materials, currencies);
        }

        if (hasArmor)
        {
            drewAny = true;
            DrawArmoursList(expansionId, armorCosts!);
        }

        if (!drewAny)
        {
            ImGui.TextColored(GoodColor, config.HideCompleteMaterials
                ? "Nothing left to farm for this expansion."
                : "No tracked materials for this expansion.");
        }
    }

    private static readonly string[] WeaponsColumns = ["Item", "Where / how to get", "Need", "Owned", "Short"];

    private void DrawWeaponsList(
        string expansionId,
        IReadOnlyList<ShoppingMaterialRow> materials,
        IReadOnlyList<MaterialDisplayRow> currencies)
    {
        if (!DrawCollapsingSection($"{expansionId}|Weapons", "Weapons", true))
        {
            return;
        }

        using var table = ImRaii.Table(
            $"WeaponsList_{expansionId}",
            5,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn(WeaponsColumns[0], ImGuiTableColumnFlags.WidthStretch, 0.36f);
        ImGui.TableSetupColumn(WeaponsColumns[1], ImGuiTableColumnFlags.WidthStretch, 0.64f);
        ImGui.TableSetupColumn(WeaponsColumns[2], ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn(WeaponsColumns[3], ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn(WeaponsColumns[4], ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableHeadersRow();

        foreach (var row in materials.OrderBy(row => row.StepOrder))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (row.Resolved)
            {
                ImGui.TextUnformatted(row.Material);
            }
            else
            {
                ImGui.TextColored(WarningColor, row.Material);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Couldn't match this to a game item, so owned can't be counted.");
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(WhereToGet(expansionId, row));

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

        foreach (var row in currencies)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(MutedColor, row.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(CurrencyWhere(row.Name));

            ImGui.TableNextColumn();
            ImGui.Text(row.Needed.ToString());

            ImGui.TableNextColumn();
            if (row.IsCurrencyTracked)
            {
                ImGui.Text(row.Owned.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "—");
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(row.IsCurrencyTracked && row.Shortfall == 0 ? GoodColor : row.IsCurrencyTracked ? BadColor : MutedColor,
                row.IsCurrencyTracked ? row.Shortfall.ToString() : "—");
        }
    }

    private void DrawArmoursList(string expansionId, IReadOnlyList<ArmorCostRow> costs)
    {
        if (!DrawCollapsingSection($"{expansionId}|Armours", "Armours — cost per set / per piece", true))
        {
            return;
        }

        ImGui.TextColored(MutedColor, "Per set = a full 5-piece set of one job/role. Need (all) = every set. Hover a stage for slot detail. Short = Need (all) − Owned.");
        ImGui.Spacing();

        using var table = ImRaii.Table(
            $"ArmoursList_{expansionId}",
            7,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
            new Vector2(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Stage", ImGuiTableColumnFlags.WidthStretch, 0.4f);
        ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("Per set", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Per piece", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Need (all)", ImGuiTableColumnFlags.WidthFixed, 76);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 72);
        ImGui.TableHeadersRow();

        foreach (var cost in costs)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.Set);
            if (!string.IsNullOrWhiteSpace(cost.Note) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(cost.Note);
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
            ImGui.TextUnformatted(cost.SetTotal > 0 ? cost.SetTotal.ToString() : "—");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.PerPiece > 0 ? cost.PerPiece.ToString() : "varies");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(cost.AllTotal > 0 ? cost.AllTotal.ToString() : "—");

            var resolved = itemIds.Count > 0;
            var owned = resolved
                ? itemIds.Aggregate(0u, (total, itemId) => total + AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly))
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

    /// <summary>Where/how to get a material: farm zone, else the reference location, else the relic step.</summary>
    private string WhereToGet(string expansionId, ShoppingMaterialRow row)
    {
        if (data.MaterialSources.TryGetValue(row.Material, out var source))
        {
            return source;
        }

        var location = FindLocation(expansionId, row.Material);
        return !string.IsNullOrWhiteSpace(location) ? location : row.Step;
    }

    private static string CurrencyWhere(string name) => name switch
    {
        "Poetics" => "Tomestones — duty roulettes / dungeons",
        "Company Seals" => "Grand Company — hunts / FATEs / turn-ins",
        "Allied Seals" => "Hunts — clan mark logs",
        "Purple Crafter Scrips" or "Purple Gatherer Scrips" => "Collectables (crafting/gathering)",
        "Orange Crafter Scrips" or "Orange Gatherer Scrips" => "Collectables (crafting/gathering)",
        "Skybuilder's Scrips" => "Ishgardian Restoration",
        "Gil" => "—",
        _ => "Wallet currency",
    };

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
