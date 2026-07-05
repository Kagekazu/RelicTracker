using Dalamud.Configuration;
using Dalamud.Plugin;
using RelicTracker.Framework;

namespace RelicTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private static readonly CharacterProgress EmptyProgress = new();

    [NonSerialized] private bool _pendingPersist;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public string SelectedExpansionId { get; set; } = "ARR";

    public ulong FfxivCollectCharacterId { get; set; }

    /// <summary>Relic progress keyed by logged-in character content ID.</summary>
    public Dictionary<ulong, CharacterProgress> ProgressByCharacter { get; set; } = new();

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

    public int Version { get; set; } = 6;

    // Legacy (v5) — migrated into ProgressByCharacter on load.
    public bool ActiveCharacterOnly { get; set; }

    public HashSet<string> RelicStepDone { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> ArmorPieceDone { get; set; } = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        MigrateIfNeeded();
    }

    /// <summary>Moves v5 global progress into the logged-in character. Safe to call on login.</summary>
    public void MigrateLegacyProgressIfNeeded() => MigrateIfNeeded();

    public CharacterProgress CurrentCharacterProgress()
    {
        ulong contentId = CharacterScope.CurrentContentId;
        if (contentId == 0)
        {
            return EmptyProgress;
        }

        if (!ProgressByCharacter.TryGetValue(contentId, out CharacterProgress? progress))
        {
            progress = new CharacterProgress();
            ProgressByCharacter[contentId] = progress;
        }

        return progress;
    }

    public void SaveInventorySnapshot(IEnumerable<string> stepKeys)
    {
        ulong contentId = CharacterScope.CurrentContentId;
        if (contentId == 0)
        {
            return;
        }

        CharacterProgress progress = CurrentCharacterProgress();
        if (progress.InventoryStepDone.SetEquals(stepKeys))
        {
            return;
        }

        progress.InventoryStepDone.Clear();
        foreach (string key in stepKeys)
        {
            progress.InventoryStepDone.Add(key);
        }

        OnSettingChanged();
    }

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

    private void MigrateIfNeeded()
    {
        if (Version >= 6)
        {
            return;
        }

        if (RelicStepDone.Count == 0 && ArmorPieceDone.Count == 0)
        {
            Version = 6;
            WriteToDisk();
            return;
        }

        ulong contentId = CharacterScope.CurrentContentId;
        if (contentId == 0)
        {
            return;
        }

        CharacterProgress progress = CurrentCharacterProgress();
        foreach (string key in RelicStepDone)
        {
            progress.RelicStepDone.Add(key);
        }

        foreach (string key in ArmorPieceDone)
        {
            progress.ArmorPieceDone.Add(key);
        }

        RelicStepDone = new HashSet<string>(StringComparer.Ordinal);
        ArmorPieceDone = new HashSet<string>(StringComparer.Ordinal);
        Version = 6;
        WriteToDisk();
    }

    private void WriteToDisk() => pluginInterface!.SavePluginConfig(this);
}
