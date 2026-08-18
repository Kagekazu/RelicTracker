using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Lumina.Excel.Sheets;

namespace RelicTracker.Framework;

/// <summary>
///     Cosmic tool stages from EXD <see cref="WKSCosmoToolClass"/> (20 items per class)
///     and live stage from CS <see cref="WKSResearchModule"/> when that module is loaded.
/// </summary>
internal static unsafe class WksCosmicTools
{
    public const string CollectType = "Cosmic Tools";

    public static void AttachStageReplicas(IReadOnlyList<RelicLine> lines)
    {
        RelicLine? cosmic = null;
        foreach (RelicLine line in lines)
        {
            if (string.Equals(line.CollectType, CollectType, StringComparison.Ordinal))
            {
                cosmic = line;
                break;
            }
        }

        if (cosmic is null)
        {
            return;
        }

        var added = 0;
        foreach (WKSCosmoToolClass row in Svc.Data.GetExcelSheet<WKSCosmoToolClass>())
        {
            List<uint> stages = StageItems(row);
            if (stages.Count == 0)
            {
                continue;
            }

            var slot = SlotForStages(cosmic, stages);
            if (slot < 0)
            {
                continue;
            }

            for (var tier = 0; tier < cosmic.TierCount; tier++)
            {
                var milestone = stages.IndexOf(cosmic.RelicId(slot, tier));
                if (milestone < 0)
                {
                    continue;
                }

                var next = stages.Count;
                if (tier + 1 < cosmic.TierCount)
                {
                    var nextMilestone = stages.IndexOf(cosmic.RelicId(slot, tier + 1));
                    if (nextMilestone > milestone)
                    {
                        next = nextMilestone;
                    }
                }

                var relicIndex = (tier * cosmic.Jobs) + slot;
                for (var stage = milestone + 1; stage < next; stage++)
                {
                    cosmic.AddReplica(relicIndex, stages[stage]);
                    added++;
                }
            }
        }

        if (added > 0)
        {
            Svc.Log.Information("[RelicTracker] Linked {Count} Cosmic tool stages from WKSCosmoToolClass.", added);
        }
    }

    public static bool CreditsStep(RelicLine line, int slot, int tier)
    {
        if (!string.Equals(line.CollectType, CollectType, StringComparison.Ordinal))
        {
            return false;
        }

        WKSManager* manager = WKSManager.Instance();
        if (manager is null || manager->ResearchModule is null || !manager->ResearchModule->IsLoaded)
        {
            return false;
        }

        if (!TryGetClassAndMilestone(line, slot, tier, out int classIndex, out int milestone))
        {
            return false;
        }

        return manager->ResearchModule->CurrentStages[classIndex] >= milestone;
    }

    private static bool TryGetClassAndMilestone(
        RelicLine line,
        int slot,
        int tier,
        out int classIndex,
        out int milestone)
    {
        classIndex = -1;
        milestone = -1;
        uint relicId = line.RelicId(slot, tier);
        if (relicId == 0)
        {
            return false;
        }

        foreach (WKSCosmoToolClass row in Svc.Data.GetExcelSheet<WKSCosmoToolClass>())
        {
            List<uint> stages = StageItems(row);
            var index = stages.IndexOf(relicId);
            if (index < 0)
            {
                continue;
            }

            classIndex = (int)row.RowId - 1;
            milestone = index;
            return classIndex >= 0;
        }

        return false;
    }

    private static int SlotForStages(RelicLine line, List<uint> stages)
    {
        for (var slot = 0; slot < line.Jobs; slot++)
        {
            for (var tier = 0; tier < line.TierCount; tier++)
            {
                uint relicId = line.RelicId(slot, tier);
                if (relicId != 0 && stages.Contains(relicId))
                {
                    return slot;
                }
            }
        }

        return -1;
    }

    private static List<uint> StageItems(WKSCosmoToolClass row)
    {
        List<uint> items = [];
        foreach (var stage in row.Stages)
        {
            uint itemId = stage.Item.RowId;
            if (itemId != 0)
            {
                items.Add(itemId);
            }
        }

        return items;
    }
}
