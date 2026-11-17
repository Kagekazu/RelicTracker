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
        Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> groups = GroupKnightshopperPurchases(materials);
        if (groups.Count == 0 && !KnightshopperIpc.IsReady)
        {
            return;
        }

        ImGui.Spacing();
        if (groups.Count == 0)
        {
            if (KnightshopperIpc.IsReady)
            {
                ImGui.TextColored(MutedColor, "None of the items you still need can be bought through Knightshopper.");
            }

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
        if (KnightshopperIpc.IsReady)
        {
            ImGui.SameLine();
            DrawKnightshopperBuyButton(selected, items, "Buy now");
        }
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

}
