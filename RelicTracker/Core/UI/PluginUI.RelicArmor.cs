using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private static readonly string[] ArmorRoleLabels =
        ["Fending", "Maiming", "Striking", "Aiming", "Scouting", "Healing", "Casting"];

    private static string ArmorRoleLabel(int pieceIndex)
    {
        var roleIndex = pieceIndex / ArmorCostCalculator.PiecesPerSet;
        return roleIndex < ArmorRoleLabels.Length ? ArmorRoleLabels[roleIndex] : $"Set {roleIndex + 1}";
    }

    private void DrawRelicArmorStatusChips(ArmorLine armor, RelicOwnership ownership)
    {
        ImGui.Spacing();
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (ArmorAutoTracked)
        {
            bool inventory = AllaganToolsIpc.IsReady;
            DrawProgressSourceChip(inventory, CollectActive);

            ImGui.SameLine();
            DrawStatusChip($"{owned}/{total} pieces", complete ? StatusChipKind.Ok : StatusChipKind.Muted);
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, DescribeProgressSource(inventory, CollectActive, "Pieces", "pieces"));
        }
        else
        {
            DrawStatusChip("Manual", StatusChipKind.Muted);
            ImGui.SameLine();
            ImGui.TextColored(MutedColor, "No auto-tracking yet — expand a set below to tick pieces, or connect Allagan Tools in Settings.");
        }
    }

    private void DrawArmorDetail(ArmorLine armor, RelicOwnership ownership)
    {
        var owned = OwnedPieces(armor, ownership);
        var total = armor.TotalPieces;
        var complete = total > 0 && owned >= total;

        if (BeginPanel("armor_header"))
        {
            ImGui.TextColored(HeaderColor, armor.LineName);
            ImGui.SameLine();
            ImGui.TextColored(complete ? GoodColor : MutedColor, $"— {owned}/{total} pieces");
            if (armor.Sets.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextColored(MutedColor, $"· {armor.Sets.Count} separate sets");
            }

            EndPanel();
        }

        var note = catalog.StepNote(armor.LineName, string.Empty);
        if (!string.IsNullOrWhiteSpace(note))
        {
            if (ImGui.CollapsingHeader("About this armor###armor_about"))
            {
                if (BeginPanel("armor_about_body"))
                {
                    ImGui.TextWrapped(note);
                    EndPanel();
                }
            }
        }

        if (BeginPanel("armor_sets"))
        {
            using (var table = ImRaii.Table(
                "ArmorSets",
                3,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg,
                new(0, 0)))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Set", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                    ImGui.TableSetupColumn("Pieces", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 160);
                    ImGui.TableHeadersRow();

                    foreach (var set in armor.Sets)
                    {
                        DrawArmorSetRows(armor, set, ownership);
                    }
                }
            }

            EndPanel();
        }

        if (ArmorAutoTracked)
        {
            DrawArmorMissingPieces(armor, ownership);
        }
        else
        {
            foreach (var set in armor.Sets)
            {
                var multiTier = set.Tiers.Count > 1;
                foreach (var tier in set.Tiers)
                {
                    var tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
                    var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
                    if (!ImGui.CollapsingHeader($"{label} ({tierOwned}/{tier.Pieces})###armor_manual_{tier.CollectType}"))
                    {
                        continue;
                    }

                    if (BeginPanel($"armor_ticks_{tier.CollectType}"))
                    {
                        DrawArmorPieceCheckboxes(armor, set, tier);
                        EndPanel();
                    }
                }
            }
        }
    }

    private void DrawArmorMissingPieces(ArmorLine armor, RelicOwnership ownership)
    {
        if (!AllaganToolsIpc.IsReady)
        {
            if (OwnedPieces(armor, ownership) < armor.TotalPieces)
            {
                ImGui.Spacing();
                ImGui.TextColored(
                    MutedColor,
                    "Connect Allagan Tools to list which pieces are missing (Collect only tracks totals).");
            }

            return;
        }

        foreach (var set in armor.Sets)
        {
            var multiTier = set.Tiers.Count > 1;
            foreach (var tier in set.Tiers)
            {
                var namedOwned = CountNamedOwnedArmorPieces(tier, ownership);
                if (namedOwned >= tier.Pieces)
                {
                    continue;
                }

                var missing = tier.Pieces - namedOwned;
                var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
                if (!ImGui.CollapsingHeader(
                        $"Pieces — {label} ({namedOwned}/{tier.Pieces}, {missing} left)###armor_pieces_{tier.CollectType}"))
                {
                    continue;
                }

                if (BeginPanel($"armor_pieces_body_{tier.CollectType}"))
                {
                    DrawArmorPieceStatusList(armor, set, tier, ownership);
                    EndPanel();
                }
            }
        }
    }

    private static int CountNamedOwnedArmorPieces(ArmorTier tier, RelicOwnership ownership)
    {
        var owned = 0;
        var count = Math.Min(tier.Pieces, tier.PieceIds.Count);
        for (var i = 0; i < count; i++)
        {
            if (ownership.IsArmorPieceOwned(tier.CollectType, i))
            {
                owned++;
            }
        }

        return owned;
    }

    private void DrawArmorPieceStatusList(ArmorLine armor, ArmorSet set, ArmorTier tier, RelicOwnership ownership)
    {
        var count = Math.Min(tier.Pieces, tier.PieceIds.Count);

        for (var i = 0; i < count; i++)
        {
            if (i % ArmorCostCalculator.PiecesPerSet == 0)
            {
                ImGui.TextColored(MutedColor, ArmorRoleLabel(i));
            }

            var owned = ownership.IsArmorPieceOwned(tier.CollectType, i);
            var pieceId = tier.PieceIds[i];
            var name = ItemDisplayNames.Resolve(pieceId, $"Piece {i + 1}");
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextColored(owned ? GoodColor : MutedColor, name);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(BuildArmorCostTooltip(armor, set, tier, i, name));
            }
        }
    }

    private void DrawArmorSetRows(ArmorLine line, ArmorSet set, RelicOwnership ownership)
    {
        var multiTier = set.Tiers.Count > 1;

        foreach (var tier in set.Tiers)
        {
            var tierOwned = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
            var fraction = tier.Pieces > 0 ? (float)tierOwned / tier.Pieces : 0f;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var label = multiTier ? $"{set.Name} — {tier.Label}" : set.Name;
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, label);
            var hovered = ImGui.IsItemHovered();

            ImGui.TableNextColumn();
            ImGui.TextColored(fraction >= 1f ? GoodColor : MutedColor, $"{tierOwned}/{tier.Pieces}");
            hovered |= ImGui.IsItemHovered();

            ImGui.TableNextColumn();
            DrawPercentBar(fraction, 150f, $"{fraction * 100f:0}%");
            hovered |= ImGui.IsItemHovered();

            if (hovered)
            {
                ImGui.SetTooltip(BuildArmorCostTooltip(line, set, tier, pieceIndex: null, tier.CollectType));
            }
        }
    }

    private void DrawArmorPieceCheckboxes(ArmorLine line, ArmorSet set, ArmorTier tier)
    {
        for (var i = 0; i < tier.Pieces; i++)
        {
            if (i % ArmorCostCalculator.PiecesPerSet == 0)
            {
                ImGui.TextColored(MutedColor, ArmorRoleLabel(i));
            }
            else
            {
                ImGui.SameLine();
            }

            bool done = config.CurrentCharacterProgress().ArmorPieceDone.Contains(ProgressKeys.ArmorPiece(tier.CollectType, i));
            if (ImGui.Checkbox($"##{tier.CollectType}_{i}", ref done))
            {
                SetArmorPieceDone(tier.CollectType, i, done);
            }

            if (ImGui.IsItemHovered())
            {
                var name = i < tier.PieceIds.Count && tier.PieceIds[i] != 0
                    ? ItemDisplayNames.Resolve(tier.PieceIds[i], $"Piece {i + 1}")
                    : $"Piece {i + 1}";
                ImGui.SetTooltip(BuildArmorCostTooltip(line, set, tier, i, name));
            }
        }
    }

    private string BuildArmorCostTooltip(
        ArmorLine line,
        ArmorSet set,
        ArmorTier tier,
        int? pieceIndex,
        string header)
    {
        List<string> lines = [header];
        if (!data.ArmorCosts.TryGetValue(line.Expansion, out var costs))
        {
            return header;
        }

        foreach (var cost in costs)
        {
            if (!ArmorCostCalculator.CostAppliesTo(cost, set.Name, tier, pieceIndex))
            {
                continue;
            }

            var currency = ItemDisplayNames.Label(cost.CurrencyIds, cost.Currency);
            if (pieceIndex is int index)
            {
                lines.Add($"{ArmorCostCalculator.PieceCost(cost, index % ArmorCostCalculator.PiecesPerSet)} {currency}");
                continue;
            }

            if (cost.PerPiece > 0 && ArmorCostCalculator.HasSplitSlotCost(cost))
            {
                var other = ArmorCostCalculator.OtherSlotCost(cost);
                lines.Add($"Per piece: {cost.PerPiece} {currency} (body/legs), {other} (other slots)");
            }
            else
            {
                lines.Add($"Per piece: {cost.PerPiece} {currency}");
            }

            if (cost.SetTotal > 0)
            {
                lines.Add($"Per set: {cost.SetTotal}");
            }
        }

        return string.Join("\n", lines);
    }

    private void SetArmorPieceDone(string collectType, int piece, bool done)
    {
        string key = ProgressKeys.ArmorPiece(collectType, piece);
        HashSet<string> armor = config.CurrentCharacterProgress().ArmorPieceDone;
        if (done)
        {
            armor.Add(key);
        }
        else
        {
            armor.Remove(key);
        }

        InvalidateOwnershipCache();
        config.OnSettingChanged();
    }
}
