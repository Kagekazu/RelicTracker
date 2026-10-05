using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private const long KnightshopperStatusPollMs = 250;

    private KnightshopperStatus? knightshopperStatus;
    private long knightshopperStatusTick;
    private string? knightshopperMessage;
    private bool knightshopperMessageIsError;

    private readonly record struct KnightshopperPurchase(uint ItemId, string Name, uint Short);

    /// <summary>Step view: one row of per-currency buy buttons under the summary.</summary>
    private void DrawKnightshopperBar(IReadOnlyList<ShoppingMaterialRow> materials)
    {
        if (!KnightshopperIpc.IsReady)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(HeaderColor, "Knightshopper");
        ImGui.SameLine();

        if (!DrawKnightshopperStatus())
        {
            Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> groups = GroupKnightshopperPurchases(materials);
            if (groups.Count == 0)
            {
                ImGui.TextColored(MutedColor, "Nothing short in this list is sold for a Knightshopper currency.");
                return;
            }

            foreach ((KnightshopperCurrency currency, List<KnightshopperPurchase> items) in groups.OrderBy(group => group.Key))
            {
                DrawKnightshopperBuyButton(currency, items, $"{KnightshopperIpc.CurrencyLabel(currency)} ({items.Count})");
                ImGui.SameLine();
            }

            ImGui.NewLine();
        }
    }

    /// <summary>
    ///     Draws the running purchase (with Cancel) or the last result. Returns true while a
    ///     purchase is running, so callers can hide their buy buttons.
    /// </summary>
    private bool DrawKnightshopperStatus()
    {
        if (!KnightshopperIpc.IsReady)
        {
            return false;
        }

        if (DrawKnightshopperProgress())
        {
            return true;
        }

        if (knightshopperMessage is { } message)
        {
            ImGui.TextColored(knightshopperMessageIsError ? BadColor : GoodColor, message);
            ImGui.SameLine();
            if (ImGui.SmallButton("x##ks_dismiss"))
            {
                knightshopperMessage = null;
            }
        }

        return false;
    }

    /// <summary>Buy button for one currency; disabled while Knightshopper is busy.</summary>
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
                + $"Knightshopper buys what's still short with {KnightshopperIpc.CurrencyLabel(currency)}:\n"
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
                knightshopperMessageIsError = finished.State != KnightshopperPurchaseState.Succeeded;
                knightshopperMessage = finished.State switch
                {
                    KnightshopperPurchaseState.Succeeded => "Knightshopper finished buying.",
                    KnightshopperPurchaseState.Cancelled => "Knightshopper run cancelled.",
                    _ => $"Knightshopper failed: {finished.Message}",
                };
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

    /// <summary>Short rows grouped by the Knightshopper currency their bundled data says buys them.</summary>
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
        if (!row.Resolved
            || row.Short == 0
            || row.Obtain != "buy"
            || !KnightshopperIpc.TryParseCurrency(row.ObtainVia, out currency)
            || !data.MaterialIdsByName.TryGetValue(row.Material, out var ids)
            || ids.Count == 0)
        {
            currency = default;
            return false;
        }

        purchase = new(ids[0], row.DisplayMaterial, row.Short);
        return true;
    }

    private void StartKnightshopper(KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items)
    {
        // Knightshopper quantities are target totals in the player's inventory, so add the
        // shortfall to what's in bags now (Owned may include retainers and quest credit).
        (uint ItemId, uint Quantity)[] request =
        [
            .. items.Select(item => (item.ItemId, GetOnCharacterItemCount(item.ItemId) + item.Short))
        ];

        knightshopperStatus = null;
        knightshopperMessageIsError = !KnightshopperIpc.TryStartItems(currency, request, out var message);
        knightshopperMessage = knightshopperMessageIsError ? message : null;
    }
}
