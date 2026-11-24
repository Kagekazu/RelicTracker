using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;
using RelicTracker.IPC;
using System.Numerics;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private string[]? ultimateJobRows;

    private void DrawUltimatesTab()
    {
        if (DrawCatalogLoadError())
        {
            return;
        }

        RefreshCollectIfStale();
        var ownership = GetOwnership();
        var lines = catalog.UltimateLines;
        if (lines.Count == 0)
        {
            ImGui.TextColored(MutedColor, "No ultimate weapons available.");
            return;
        }

        var owned = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            for (int slot = 0; slot < lines[i].Jobs; slot++)
            {
                if (ownership.IsStepDone(lines[i], slot, 0))
                {
                    owned[i]++;
                }
            }
        }

        int total = lines.Sum(line => line.Jobs);
        ImGui.TextColored(HeaderColor, "Ultimate Weapons");
        ImGui.SameLine();
        ImGui.TextColored(owned.Sum() == total ? GoodColor : MutedColor, $"{owned.Sum()}/{total} owned");

        bool inventoryLinked = MogVaultIpc.IsReady;
        if (inventoryLinked || CollectActive)
        {
            DrawProgressSourceChip(inventoryLinked, CollectActive);
        }
        else
        {
            DrawStatusChip("Saved progress", StatusChipKind.Muted);
        }

        DrawProgressRecheckButton();
        if (!inventoryLinked)
        {
            ImGui.TextWrapped("MogVault is off. Showing saved progress.");
        }

        EndStickyHeader();

        ultimateJobRows ??= UltimateJobRows(lines);
        float iconSize = ImGui.GetTextLineHeightWithSpacing() * 1.6f;
        using var table = ImRaii.Table(
            "UltimatesGrid",
            1 + lines.Count,
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY,
            new(0, -1));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("WWWW").X);
        foreach (var line in lines)
        {
            ImGui.TableSetupColumn(line.ShortName, ImGuiTableColumnFlags.WidthFixed, Math.Max(iconSize, ImGui.CalcTextSize("00/00").X));
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();
        for (int i = 0; i < lines.Count; i++)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(HeaderColor, lines[i].ShortName);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(lines[i].CollectType);
            }

            ImGui.TextColored(owned[i] == lines[i].Jobs ? GoodColor : MutedColor, $"{owned[i]}/{lines[i].Jobs}");
        }

        var items = Svc.Data.GetExcelSheet<Item>();
        foreach (var job in ultimateJobRows)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, iconSize);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(job);
            foreach (var line in lines)
            {
                ImGui.TableNextColumn();
                int slot = IndexOfJob(line.EffectiveJobList, job);
                uint itemId = slot >= 0 ? line.RelicId(slot, 0) : 0;
                if (itemId == 0 || !items.TryGetRow(itemId, out var item))
                {
                    continue;
                }

                bool has = ownership.IsStepDone(line, slot, 0);
                if (item.Icon != 0
                    && Svc.Texture.TryGetFromGameIcon(new GameIconLookup(item.Icon), out var texture)
                    && texture.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Image(wrap.Handle, new Vector2(iconSize), Vector2.Zero, Vector2.One,
                        has ? Vector4.One : new Vector4(1f, 1f, 1f, 0.25f));
                }
                else
                {
                    ImGui.Dummy(new Vector2(iconSize));
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"{item.Name}\n{(has ? "Owned" : "Not owned")}");
                }
            }
        }
    }

    private static string[] UltimateJobRows(IReadOnlyList<RelicLine> lines)
    {
        Dictionary<string, int> priority = new(StringComparer.Ordinal);
        foreach (var job in GameSheets.English<ClassJob>())
        {
            priority.TryAdd(job.Abbreviation.ToString(), job.UIPriority);
        }

        return
        [
            .. lines.SelectMany(line => line.EffectiveJobList)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(job => priority.GetValueOrDefault(job, int.MaxValue))
        ];
    }
}
