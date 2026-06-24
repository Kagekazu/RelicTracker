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
        var ownedLookup = (Func<uint, uint>)(itemId => AllaganToolsIpc.GetOwnedCount(itemId, config.ActiveCharacterOnly));
        var materials = data.GetShoppingMaterials(expansionId, statuses, itemResolver, ownedLookup);
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

        var drewAny = false;
        foreach (var group in materials
                     .GroupBy(row => (row.StepOrder, row.Step))
                     .OrderBy(group => group.Key.StepOrder))
        {
            drewAny = true;
            DrawShoppingStepGroup(expansionId, group.Key.Step, group.ToList());
        }

        if (currencies.Count > 0)
        {
            drewAny = true;
            DrawCurrencyGroup(expansionId, currencies);
        }

        if (!drewAny)
        {
            ImGui.TextColored(GoodColor, config.HideCompleteMaterials
                ? "Nothing left to farm for this expansion."
                : "No tracked materials for this expansion.");
        }
    }

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

    private void DrawShoppingStepGroup(string expansionId, string step, IReadOnlyList<ShoppingMaterialRow> rows)
    {
        var shortCount = rows.Count(row => row.Short > 0);
        var header = shortCount > 0 ? $"{step} ({shortCount} needed)" : step;
        if (!DrawCollapsingSection($"{expansionId}|{step}", header, shortCount > 0))
        {
            return;
        }

        using var table = ImRaii.Table(
            $"Shopping_{expansionId}_{step}",
            4,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.ScrollY,
            new Vector2(0, Math.Min(280f, (rows.Count + 1) * ImGui.GetTextLineHeightWithSpacing() + 8f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
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
    }

    private void DrawCurrencyGroup(string expansionId, IReadOnlyList<MaterialDisplayRow> currencies)
    {
        var shortCount = currencies.Count(row => row.Shortfall > 0);
        var header = shortCount > 0 ? $"Currencies ({shortCount} short)" : "Currencies";
        if (!DrawCollapsingSection($"{expansionId}|Currencies", header, shortCount > 0))
        {
            return;
        }

        using var table = ImRaii.Table(
            $"ShoppingCurrencies_{expansionId}",
            4,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.ScrollY,
            new Vector2(0, Math.Min(200f, (currencies.Count + 1) * ImGui.GetTextLineHeightWithSpacing() + 8f)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableSetupColumn("Short", ImGuiTableColumnFlags.WidthFixed, 64);
        ImGui.TableHeadersRow();

        foreach (var row in currencies)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Name);

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
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Owned count isn't available for this currency.");
                }
            }

            ImGui.TableNextColumn();
            if (row.IsCurrencyTracked)
            {
                ImGui.TextColored(row.Shortfall == 0 ? GoodColor : BadColor, row.Shortfall.ToString());
            }
            else
            {
                ImGui.TextColored(MutedColor, "—");
            }
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
