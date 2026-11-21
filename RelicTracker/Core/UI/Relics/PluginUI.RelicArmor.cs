using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private static readonly string[] ArmorRoleLabels =
        ["Fending", "Maiming", "Striking", "Aiming", "Scouting", "Healing", "Casting"];

    private void DrawRelicArmorStatusChips(ArmorLine armor, RelicOwnership ownership)
    {
        ImGui.Spacing();
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (ArmorAutoTracked)
        {
            bool inventory = MogVaultIpc.IsReady;
            DrawProgressSourceChip(inventory, CollectActive);

            ImGui.SameLine();
            DrawStatusChip($"{owned}/{total} pieces", complete ? StatusChipKind.Ok : StatusChipKind.Muted);
        }
        else
        {
            DrawStatusChip("Saved progress", StatusChipKind.Muted);
            ImGui.SameLine();
            ImGui.TextWrapped("Enable MogVault or link FFXIV Collect in Setup to update your progress.");
        }
    }

    private (ArmorLine Line, ulong Character)? armorSelection;
    private int armorSetIndex;

    // Set by a right-click on an armour piece: open its set, tier and role instead of the defaults.
    private (string Set, int Tier, int Piece)? pendingArmorFocus;
    private int? inspectedArmorTier;

    private string ArmorPieceCostLabel(ArmorLine line, ArmorSet set, ArmorTier tier, int pieceIndex)
    {
        if (!data.ArmorCosts.TryGetValue(line.Expansion, out var costs))
        {
            return string.Empty;
        }

        List<string> labels = [];
        foreach (var cost in costs)
        {
            if (!ArmorCostCalculator.CostAppliesTo(cost, set.Name, tier, pieceIndex))
            {
                continue;
            }

            var currency = ItemDisplayNames.Label(cost.CurrencyIds, cost.Currency);
            uint quantity = ArmorCostCalculator.PieceCost(cost, pieceIndex % ArmorCostCalculator.PiecesPerSet);
            labels.Add($"{quantity:N0} {currency}");
        }

        return string.Join(" · ", labels);
    }
}
