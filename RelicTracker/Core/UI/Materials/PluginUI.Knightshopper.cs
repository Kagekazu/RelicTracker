using RelicTracker.IPC;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private const long KnightshopperStatusPollMs = 250;

    private KnightshopperStatus? knightshopperStatus;
    private long knightshopperStatusTick;
    private StatusMessage? knightshopperResult;
    private Task<(bool Saved, string Message)>? knightshopperSync;
    private KnightshopperCurrency? knightshopperSelectedCurrency;

    private readonly record struct KnightshopperPurchase(uint ItemId, string Name, uint Short);

    private void DrawKnightshopperBar(IReadOnlyList<ShoppingMaterialRow> materials, string? listPurpose = null)
    {
        if (!KnightshopperIpc.IsReady)
        {
            return;
        }

        Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> groups = GroupKnightshopperPurchases(materials);
        ImGui.Spacing();
        if (groups.Count == 0)
        {
            ImGui.TextColored(MutedColor, "None of the items you still need can be bought through Knightshopper.");
            return;
        }

        var currencies = groups.Keys.OrderBy(currency => currency).ToArray();
        var selected = knightshopperSelectedCurrency is { } previous && groups.ContainsKey(previous)
            ? previous
            : currencies[0];

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(HeaderColor, "Knightshopper");
        ImGui.SameLine();

        if (currencies.Length == 1)
        {
            ImGui.TextUnformatted(KnightshopperIpc.CurrencyLabel(selected));
        }
        else
        {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 13);
            using var combo = ImRaii.Combo("##KnightshopperCurrency", KnightshopperIpc.CurrencyLabel(selected));
            if (combo)
            {
                foreach (var currency in currencies)
                {
                    if (ImGui.Selectable(KnightshopperIpc.CurrencyLabel(currency), currency == selected))
                    {
                        selected = currency;
                    }
                }
            }
        }

        knightshopperSelectedCurrency = selected;
        var items = groups[selected];
        ImGui.SameLine();
        DrawKnightshopperSaveButton(selected, items, listPurpose);
        ImGui.SameLine();
        DrawKnightshopperBuyButton(selected, items, "Buy now");
    }

    // True while a purchase is running, so callers can hide their buy buttons.
    private bool DrawKnightshopperStatus()
    {
        if (knightshopperSync is { IsCompleted: true } sync)
        {
            knightshopperSync = null;
            if (sync.IsCompletedSuccessfully)
            {
                var result = sync.Result;
                knightshopperResult = new(result.Message, !result.Saved);
            }
            else
            {
                knightshopperResult = new("Saving was interrupted. Try again.", true);
            }
        }

        if (KnightshopperIpc.IsReady && DrawKnightshopperProgress())
        {
            return true;
        }

        DrawStatusMessage(ref knightshopperResult, "ks_dismiss");
        return false;
    }

    private void DrawKnightshopperSaveButton(
        KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items, string? listPurpose = null,
        string label = "Save shopping list")
    {
        var scope = string.IsNullOrEmpty(config.TrackerLineFilter)
            ? ExpansionNames.LongName(config.SelectedExpansionId)
            : config.TrackerLineFilter;
        var name = $"RelicTracker materials - {scope}";
        if (listPurpose is not null)
        {
            name = $"{name} - {listPurpose}";
        }
        using (ImRaii.Disabled(knightshopperSync is not null))
        {
            if (ImGui.SmallButton($"{label}##ks_save_{currency}"))
            {
                ulong character = CharacterScope.CurrentContentId;
                var itemsToSave = items.ToArray();
                knightshopperSync = Svc.Framework.RunOnFrameworkThread(() =>
                    character == 0 || character != CharacterScope.CurrentContentId
                        ? (false, "Your character changed. Reopen this page and save the list.")
                        : KnightshopperIpc.SyncList(currency, name, KnightshopperTargets(itemsToSave)));
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                $"Find {name} in Knightshopper under {KnightshopperIpc.CurrencyLabel(currency)}.\n"
                + "Saving again replaces that shopping list. Nothing is purchased.\n\n"
                + string.Join("\n", items.Select(item => $"  {item.Short:N0} × {item.Name}")));
        }
    }

    private void DrawKnightshopperBuyButton(KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items, string label)
    {
        using (ImRaii.Disabled(KnightshopperIpc.IsBusy()))
        {
            if (ImGui.SmallButton($"{label}##ks_{currency}"))
            {
                StartKnightshopper(currency, items);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                (KnightshopperIpc.IsBusy() ? "Knightshopper is busy.\n\n" : string.Empty)
                + $"Starts buying these items with {KnightshopperIpc.CurrencyLabel(currency)} right away:\n"
                + string.Join("\n", items.Select(item => $"  {item.Short:N0} × {item.Name}")));
        }
    }

    private bool DrawKnightshopperProgress()
    {
        if (KnightshopperIpc.ActiveOperation is null)
        {
            return false;
        }

        long now = Environment.TickCount64;
        if (knightshopperStatus is null || now - knightshopperStatusTick >= KnightshopperStatusPollMs)
        {
            knightshopperStatus = KnightshopperIpc.GetActiveStatus();
            knightshopperStatusTick = now;
        }

        if (knightshopperStatus is not { } status || status.State == KnightshopperPurchaseState.Unknown || status.IsFinished)
        {
            if (knightshopperStatus is { IsFinished: true } finished)
            {
                knightshopperResult = new(
                    finished.State switch
                    {
                        KnightshopperPurchaseState.Succeeded => "Knightshopper finished buying.",
                        KnightshopperPurchaseState.Cancelled => "Knightshopper run cancelled.",
                        _ => $"Knightshopper failed: {finished.Message}",
                    },
                    finished.State != KnightshopperPurchaseState.Succeeded);
            }

            KnightshopperIpc.ClearActive();
            knightshopperStatus = null;
            return false;
        }

        var currencyLabel = KnightshopperIpc.ActiveCurrency is { } currency ? KnightshopperIpc.CurrencyLabel(currency) : "items";
        var item = status.CurrentItemId != 0 ? ItemDisplayNames.Label([status.CurrentItemId], string.Empty) : string.Empty;
        ImGui.TextColored(
            WarningColor,
            $"Knightshopper buying with {currencyLabel}: {Math.Min(status.CurrentIndex + 1, status.TotalItems)}/{status.TotalItems} {item}".TrimEnd());
        ImGui.SameLine();
        using (ImRaii.Disabled(status.State == KnightshopperPurchaseState.CancellationRequested))
        {
            if (ImGui.SmallButton("Cancel##ks_cancel"))
            {
                KnightshopperIpc.CancelActive();
            }
        }

        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            ImGui.TextColored(MutedColor, status.Message);
        }

        return true;
    }

    private Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> GroupKnightshopperPurchases(
        IReadOnlyList<ShoppingMaterialRow> materials)
    {
        Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> groups = [];
        foreach (var row in MergeByMaterial(materials))
        {
            if (TryGetKnightshopperPurchase(row, out var currency, out var purchase))
            {
                if (!groups.TryGetValue(currency, out var list))
                {
                    list = [];
                    groups[currency] = list;
                }

                list.Add(purchase);
            }
        }

        foreach (var list in groups.Values)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        return groups;
    }

    private bool TryGetKnightshopperPurchase(
        ShoppingMaterialRow row,
        out KnightshopperCurrency currency,
        out KnightshopperPurchase purchase)
    {
        purchase = default;
        var itemId = PrimaryItemId(row);
        if (!row.Resolved
            || row.Short == 0
            || itemId == 0
            || !VendorAlternativeUnlocked(row)
            || !KnightshopperIpc.TryParseCurrency(row.Obtain == "buy" ? row.ObtainVia : row.BuyAlternative, out currency))
        {
            currency = default;
            return false;
        }

        purchase = new(itemId, row.DisplayMaterial, row.Short);
        return true;
    }

    private static bool VendorAlternativeUnlocked(ShoppingMaterialRow row) =>
        row.BuyAlternativeQuest == 0
        || (CharacterScope.CurrentContentId != 0 && QuestManager.IsQuestComplete(row.BuyAlternativeQuest));

    private void StartKnightshopper(KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items)
    {
        var request = KnightshopperTargets(items);

        knightshopperStatus = null;
        knightshopperResult = KnightshopperIpc.TryStartItems(currency, request, out var message) ? null : new(message, true);
    }
    // The provider takes bag inventory targets; the shortfall already accounts for other sources.
    private static (uint ItemId, uint Quantity)[] KnightshopperTargets(IReadOnlyList<KnightshopperPurchase> items) =>
        [.. items.Select(item => (item.ItemId, GetOnCharacterItemCount(item.ItemId) + item.Short))];

    private List<KnightshopperPurchase> GetPhantomArmorPurchases(
        ArmorLine armor, ArmorSet set, ArmorTier tier, RelicOwnership ownership, Func<uint, uint> ownedLookup)
    {
        var missing = Enumerable.Range(0, tier.Pieces)
            .Where(index => !ownership.IsArmorPieceOwned(tier.CollectType, index))
            .ToArray();
        if (missing.Length == 0)
        {
            return [];
        }

        return tier.Label == "Base"
            ? PhantomBasePiecePurchases(tier, missing)
            : PhantomUpgradeMaterialPurchases(armor, set, tier, missing, ownedLookup);
    }

    private void DrawPhantomArmorShoppingButton(ArmorSet set, ArmorTier tier, IReadOnlyList<KnightshopperPurchase> purchases)
    {
        if (!KnightshopperIpc.IsReady)
        {
            return;
        }

        DrawKnightshopperSaveButton(KnightshopperCurrency.OccultCrescent, purchases, $"{set.Name} {tier.Label}",
            tier.Label == "Base" ? "Save missing pieces to Knightshopper" : "Save upgrade materials to Knightshopper");
    }

    private static List<KnightshopperPurchase> PhantomBasePiecePurchases(ArmorTier tier, IReadOnlyList<int> missing) =>
    [
        .. missing.Where(index => index < tier.PieceIds.Count)
            .Select(index => tier.PieceIds[index])
            .Where(id => id != 0)
            .Distinct()
            .Select(id => new KnightshopperPurchase(id, ItemDisplayNames.Resolve(id, $"Item {id}"), 1)),
    ];

    private List<KnightshopperPurchase> PhantomUpgradeMaterialPurchases(
        ArmorLine armor, ArmorSet set, ArmorTier tier, IReadOnlyList<int> missing, Func<uint, uint> ownedLookup)
    {
        if (!data.ArmorCosts.TryGetValue(armor.Expansion, out var costs))
        {
            return [];
        }

        List<KnightshopperPurchase> purchases = [];
        foreach (var cost in costs)
        {
            uint itemId = cost.CurrencyIds.FirstOrDefault();
            // These materials have direct offers for Knightshopper's supported Occult Crescent currencies.
            if (itemId is not (47864 or 47865 or 51978)
                || !ArmorCostCalculator.CostAppliesTo(cost, set.Name, tier, null))
            {
                continue;
            }

            uint need = missing.Aggregate(0u, (sum, piece) =>
                sum + ArmorCostCalculator.PieceCost(cost, piece % ArmorCostCalculator.PiecesPerSet));
            uint owned = ownedLookup(itemId);
            if (owned < need)
            {
                purchases.Add(new(itemId, ItemDisplayNames.Resolve(itemId, cost.Currency), need - owned));
            }
        }
        return purchases;
    }
}
