using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private const long KnightshopperStatusPollMs = 250;

    private KnightshopperStatus? knightshopperStatus;
    private long knightshopperStatusTick;
    private StatusMessage? knightshopperResult;

    private readonly record struct KnightshopperPurchase(uint ItemId, string Name, uint Short, uint Need);

    /// <summary>Step view: per-currency copy/buy buttons under the summary.</summary>
    private void DrawKnightshopperBar(IReadOnlyList<ShoppingMaterialRow> materials)
    {
        Dictionary<KnightshopperCurrency, List<KnightshopperPurchase>> groups = GroupKnightshopperPurchases(materials);
        if (groups.Count == 0 && !KnightshopperIpc.IsReady && knightshopperResult is null)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(HeaderColor, "Knightshopper");
        ImGui.SameLine();

        if (!DrawKnightshopperStatus())
        {
            if (groups.Count == 0)
            {
                if (KnightshopperIpc.IsReady)
                {
                    ImGui.TextColored(MutedColor, "Nothing short in this list is sold for a Knightshopper currency.");
                }

                return;
            }

            foreach ((KnightshopperCurrency currency, List<KnightshopperPurchase> items) in groups.OrderBy(group => group.Key))
            {
                DrawKnightshopperCopyButton(currency, items);
                if (KnightshopperIpc.IsReady)
                {
                    ImGui.SameLine();
                    DrawKnightshopperBuyButton(
                        currency,
                        items,
                        $"{KnightshopperIpc.CurrencyLabel(currency)} ({items.Count})");
                }

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
            DrawStatusMessage(ref knightshopperResult, "ks_dismiss");
            return false;
        }

        if (DrawKnightshopperProgress())
        {
            return true;
        }

        DrawStatusMessage(ref knightshopperResult, "ks_dismiss");
        return false;
    }

    /// <summary>Copy (and optional Buy) for one currency group on the same line.</summary>
    private void DrawKnightshopperCurrencyActions(
        KnightshopperCurrency currency,
        IReadOnlyList<KnightshopperPurchase> items,
        bool showBuy)
    {
        DrawKnightshopperCopyButton(currency, items);
        if (showBuy)
        {
            ImGui.SameLine();
            DrawKnightshopperBuyButton(currency, items, $"Buy {items.Count} with Knightshopper");
        }
    }

    /// <summary>Copies Need amounts as JSON for Knightshopper's shopping-list Paste.</summary>
    private void DrawKnightshopperCopyButton(KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items)
    {
        if (ImGui.SmallButton($"Copy for Knightshopper##ks_copy_{currency}"))
        {
            CopyKnightshopperList(currency, items);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                $"Copy Need amounts for {KnightshopperIpc.CurrencyLabel(currency)} to the clipboard.\n"
                + "In Knightshopper, open the same currency page, then Paste into the shopping-list dropdown.\n"
                + "Needs a Knightshopper build with the JSON list importer.\n\n"
                + string.Join("\n", items.Select(item => $"  {item.Need:N0} × {item.Name}")));
        }
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
        var itemId = PrimaryItemId(row);
        if (!row.Resolved
            || row.Short == 0
            || row.Obtain != "buy"
            || itemId == 0
            || !KnightshopperIpc.TryParseCurrency(row.ObtainVia, out currency))
        {
            currency = default;
            return false;
        }

        purchase = new(itemId, row.DisplayMaterial, row.Short, row.Need);
        return true;
    }

    private void CopyKnightshopperList(KnightshopperCurrency currency, IReadOnlyList<KnightshopperPurchase> items)
    {
        try
        {
            var entries = items.Select(item => new KnightshopperListItem(item.ItemId, item.Need));
            var name = $"Relic Tracker — {KnightshopperIpc.CurrencyLabel(currency)}";
            if (name.Length > 100)
            {
                name = name[..100];
            }

            ImGui.SetClipboardText(KnightshopperListExport.Encode(currency, name, entries));
            knightshopperResult = new($"Copied {items.Count} items for Knightshopper ({KnightshopperIpc.CurrencyLabel(currency)}).", false);
        }
        catch (ArgumentException exception)
        {
            Svc.Log.Warning(exception, "[RelicTracker] Could not export Knightshopper list.");
            knightshopperResult = new(exception.Message, true);
        }
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
        knightshopperResult = KnightshopperIpc.TryStartItems(currency, request, out var message) ? null : new(message, true);
    }
}
