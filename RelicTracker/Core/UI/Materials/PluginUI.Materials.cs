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

        ImGui.TextColored(HeaderColor, "Materials");
        float controlWidth = Math.Min(208, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2);
        ImGui.SetNextItemWidth(controlWidth);
        using (var expansionPicker = ImRaii.Combo("##MaterialExpansion", ExpansionNames.LongName(config.SelectedExpansionId)))
        {
            if (expansionPicker)
            {
                foreach (var expansion in data.Manifest.Expansions)
                {
                    if (ImGui.Selectable(ExpansionNames.LongName(expansion), expansion == config.SelectedExpansionId))
                    {
                        config.SelectedExpansionId = expansion;
                        config.TrackerLineFilter = string.Empty;
                        config.OnSettingChanged();
                    }
                }
            }
        }

        string[] scopes = [.. catalog.LinesFor(config.SelectedExpansionId).Select(line => line.CollectType),
            .. catalog.ArmorLinesFor(config.SelectedExpansionId).Select(armor => armor.LineName)];
        if (config.TrackerLineFilter.Length > 0 && !scopes.Contains(config.TrackerLineFilter, StringComparer.Ordinal))
        {
            config.TrackerLineFilter = string.Empty;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(controlWidth);
        using (var scopePicker = ImRaii.Combo("##MaterialScope",
            config.TrackerLineFilter.Length == 0 ? "All relics in expansion" : config.TrackerLineFilter))
        {
            if (scopePicker)
            {
                if (ImGui.Selectable("All relics in expansion", config.TrackerLineFilter.Length == 0))
                {
                    config.TrackerLineFilter = string.Empty;
                    config.OnSettingChanged();
                }
                foreach (var scope in scopes)
                {
                    if (ImGui.Selectable(scope, scope == config.TrackerLineFilter))
                    {
                        config.TrackerLineFilter = scope;
                        config.OnSettingChanged();
                    }
                }
            }
        }

        ImGui.SetNextItemWidth(controlWidth);
        ImGui.InputTextWithHint("##MaterialSearch", "Find a material", ref materialFilter, 128);
        ImGui.SameLine();
        bool showOwned = !config.HideCompleteMaterials;
        if (ImGui.Checkbox("Include ready items", ref showOwned))
        {
            config.HideCompleteMaterials = !showOwned;
            config.OnSettingChanged();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Also show materials you already have enough of.");
        }

        EndStickyHeader();

        DrawMaterialsPlan(config.SelectedExpansionId);
    }

    private IReadOnlyList<RelicLineStatus> TrackerStatuses(string expansionId, string? lineFilter) =>
    [
        .. GetStatuses()
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
        IReadOnlyList<RelicLineStatus> statuses = GetStatuses();
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
            "Rewards you already have count toward the materials needed for those upgrades.",
            ShoppingTableFlags,
            ownedColumnWidth: 64,
            showResolvedTooltip: true);
    }

    private (uint Inventory, uint ArmorCredit, uint Owned) ArmorCurrencyOwned(
        string expansionId,
        ArmorCostRow cost,
        Func<uint, uint> ownedLookup)
    {
        uint inventory = ShoppingListBuilder.SumOwned(cost.CurrencyIds, ownedLookup);
        uint armorCredit = ArmorCostCalculator.ArmorPieceCredit(expansionId, cost, catalog, ownedLookup, GetOwnership());
        return (inventory, armorCredit, inventory + armorCredit);
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private bool DrawCollapsingSection(string configKey, string header, bool defaultOpen)
    {
        var isOpen = config.ExpandedMaterialSections.TryGetValue(configKey, out var saved) ? saved : defaultOpen;
        var nodeOpen = ImGui.CollapsingHeader(header, isOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (nodeOpen != isOpen)
        {
            config.ExpandedMaterialSections[configKey] = nodeOpen;
            config.OnSettingChanged();
        }

        return nodeOpen;
    }
}
