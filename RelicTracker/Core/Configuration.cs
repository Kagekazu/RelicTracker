using Dalamud.Configuration;
using Dalamud.Plugin;
namespace RelicTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized] private bool _pendingPersist;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public string SelectedExpansionId { get; set; } = "ARR";

    /// <summary>When true, only count items on the active character (+ retainers). Otherwise all tracked characters.</summary>
    public bool ActiveCharacterOnly { get; set; }

    public ulong FfxivCollectCharacterId { get; set; }

    /// <summary>Per-job step completions in the Relic detail view: collectType|job|tierIndex</summary>
    public HashSet<string> RelicStepDone { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Manual armor piece ticks (used when FFXIV Collect is not linked): collectType|pieceIndex</summary>
    public HashSet<string> ArmorPieceDone { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Last selected line + job in the Relic detail view, for convenience.</summary>
    public string DetailExpansionId { get; set; } = "ARR";

    public string DetailCollectType { get; set; } = string.Empty;

    public string DetailJob { get; set; } = string.Empty;

    /// <summary>Hide materials you already have enough of.</summary>
    public bool HideCompleteMaterials { get; set; } = true;

    /// <summary>On the Overview tab, hide relic lines you have already finished on every job.</summary>
    public bool OverviewIncompleteOnly { get; set; }

    /// <summary>Tracker focus: a CollectType to scope the shopping list to one relic line ("" = all lines).</summary>
    public string TrackerLineFilter { get; set; } = string.Empty;

    public Dictionary<string, bool> ExpandedMaterialSections { get; set; } = new(StringComparer.Ordinal);

    public int Version { get; set; } = 5;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save()
    {
        WriteToDisk();
        _pendingPersist = false;
    }

    public void OnSettingChanged() => _pendingPersist = true;

    public void PersistIfDirty()
    {
        if (!_pendingPersist)
        {
            return;
        }

        WriteToDisk();
        _pendingPersist = false;
    }

    private void WriteToDisk() => pluginInterface!.SavePluginConfig(this);
}
