using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;
using RelicTracker.IPC;
using System.Numerics;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private string collectionSearch = string.Empty;
    private float collectionScroll;
    private bool restoreCollectionScroll;

    private void DrawCollection()
    {
        if (DrawCatalogLoadError())
        {
            return;
        }

        RefreshCollectIfStale();
        var ownership = GetOwnership();
        var statuses = RelicStatusService.Build(ownership, catalog, config.HidePhyseosRelics);
        string query = collectionSearch.Trim();

        ImGui.TextColored(HeaderColor, "All Relics");
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"{statuses.Count + catalog.Expansions.Sum(id => catalog.ArmorLinesFor(id).Count())} series");
        ImGui.SetNextItemWidth(Math.Min(340, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputTextWithHint("##CollectionSearch", "Search relics, expansions or jobs", ref collectionSearch, 128))
        {
            query = collectionSearch.Trim();
            collectionScroll = 0;
            restoreCollectionScroll = true;
        }

        if (!MogVaultIpc.IsReady)
        {
            ImGui.TextWrapped("Inventory is offline. Showing saved progress.");
        }

        using var scroll = ImRaii.Child("##CollectionScroll", new(0, -1), false);
        if (!scroll)
        {
            return;
        }

        if (restoreCollectionScroll)
        {
            ImGui.SetScrollY(collectionScroll);
            restoreCollectionScroll = false;
        }
        else
        {
            collectionScroll = ImGui.GetScrollY();
        }

        bool anyMatch = false;
        foreach (var expansion in catalog.Expansions)
        {
            var title = ExpansionNames.LongName(expansion);
            bool wholeExpansion = Matches(title, query) || Matches(expansion, query);
            var lines = statuses.Where(status => status.Line.Expansion == expansion
                && (wholeExpansion || Matches(status.Line.CollectType, query)
                    || status.Line.EffectiveJobList.Any(job => Matches(job, query)))).ToArray();
            var armors = catalog.ArmorLinesFor(expansion).Where(armor => wholeExpansion
                || Matches(armor.LineName, query) || armor.Sets.Any(set => Matches(set.Name, query))).ToArray();

            if (lines.Length == 0 && armors.Length == 0)
            {
                continue;
            }

            anyMatch = true;
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(HeaderColor, title);
            foreach (var status in lines)
            {
                var line = status.Line;
                var kind = ExpansionNames.IsTools(expansion) ? "Tools" : "Weapons";
                DrawCollectionEntry(expansion, line.CollectType, kind,
                    line.RelicIds.FirstOrDefault(id => id != 0), status.JobsComplete, line.Jobs, status.Percent);
            }

            foreach (var armor in armors)
            {
                int owned = OwnedPieces(armor, ownership);
                int total = armor.TotalPieces;
                uint iconItem = armor.AllTiers.SelectMany(tier => tier.PieceIds).FirstOrDefault(id => id != 0);
                DrawCollectionEntry(expansion, armor.LineName, "Armour", iconItem, owned, total,
                    total > 0 ? (float)owned / total : 0);
            }
        }

        if (!anyMatch)
        {
            ImGui.TextUnformatted("No relics match your search.");
            if (ImGui.Button("Clear search"))
            {
                collectionSearch = string.Empty;
            }
        }
    }

    private void DrawCollectionEntry(string expansion, string name, string kind, uint itemId, int done, int total, float progress)
    {
        using var id = ImRaii.PushId($"{expansion}/{name}/{kind}");
        float rowWidth = Math.Min(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 34f);
        using var row = ImRaii.Table("##Series", 3, ImGuiTableFlags.SizingStretchProp, new Vector2(rowWidth, 0));
        if (!row)
        {
            return;
        }

        float height = ImGui.GetTextLineHeightWithSpacing() * 1.6f;
        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, height);
        ImGui.TableSetupColumn("Series", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawSeriesIcon(itemId, height);

        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{name}##Open", false, ImGuiSelectableFlags.None, new Vector2(0, height)))
        {
            OpenTo(new RelicItemTarget(RelicTrackerDestinationTab.Relic, expansion, string.Empty, name));
        }

        ImGui.TableNextColumn();
        ImGui.TextColored(done == total && total > 0 ? GoodColor : MutedColor, $"{done}/{total} · {kind}");
        using var fill = ImRaii.PushColor(ImGuiCol.PlotHistogram, progress >= 1f ? GoodColor : ProgressColor);
        ImGui.ProgressBar(Math.Clamp(progress, 0f, 1f), new Vector2(-1, ImGui.GetTextLineHeight() * 0.35f), string.Empty);
    }

    private static void DrawSeriesIcon(uint itemId, float size)
    {
        var items = Svc.Data.GetExcelSheet<Item>();
        if (itemId != 0 && items.TryGetRow(itemId, out var item) && item.Icon != 0
            && Svc.Texture.TryGetFromGameIcon(new GameIconLookup(item.Icon), out var texture)
            && texture.TryGetWrap(out var wrap, out _))
        {
            ImGui.Image(wrap.Handle, new Vector2(size));
        }
        else
        {
            ImGui.Dummy(new Vector2(size));
        }
    }

    private static bool Matches(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static int OwnedPieces(ArmorLine armor, RelicOwnership ownership) =>
        armor.AllTiers.Sum(tier => ownership.OwnedPieceCount(tier.CollectType, tier.Pieces));
}
