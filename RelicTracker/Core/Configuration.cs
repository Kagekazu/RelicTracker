using Dalamud.Configuration;
using Dalamud.Plugin;

namespace RelicTracker;

[Serializable]
public sealed class CharacterProgress
{
    public HashSet<string> RelicStepDone { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> ArmorPieceDone { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> InventoryStepDone { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> InventoryArmorPieceDone { get; set; } = new(StringComparer.Ordinal);
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private static readonly CharacterProgress EmptyProgress = new();

    [NonSerialized] private bool _pendingPersist;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public string SelectedExpansionId { get; set; } = "ARR";

    public ulong FfxivCollectCharacterId { get; set; }

    public Dictionary<ulong, CharacterProgress> ProgressByCharacter { get; set; } = new();

    public string DetailExpansionId { get; set; } = "ARR";

    public string DetailCollectType { get; set; } = string.Empty;

    public string DetailJob { get; set; } = string.Empty;

    public bool HideCompleteMaterials { get; set; } = true;

    public bool OverviewIncompleteOnly { get; set; }

    public bool HidePhyseosRelics { get; set; }

    public string TrackerLineFilter { get; set; } = string.Empty;

    public Dictionary<string, bool> ExpandedMaterialSections { get; set; } = new(StringComparer.Ordinal);

    public int Version { get; set; } = 6;

    public HashSet<string> RelicStepDone { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> ArmorPieceDone { get; set; } = new(StringComparer.Ordinal);

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        MigrateLegacyProgressIfNeeded();
    }

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

    public void SaveInventorySnapshot(IEnumerable<string> stepKeys, IEnumerable<string> armorPieceKeys)
    {
        ulong contentId = CharacterScope.CurrentContentId;
        if (contentId == 0)
        {
            return;
        }

        CharacterProgress progress = CurrentCharacterProgress();
        bool stepsDirty = !progress.InventoryStepDone.SetEquals(stepKeys);
        bool armorDirty = !progress.InventoryArmorPieceDone.SetEquals(armorPieceKeys);
        if (!stepsDirty && !armorDirty)
        {
            return;
        }

        if (stepsDirty)
        {
            progress.InventoryStepDone.Clear();
            foreach (string key in stepKeys)
            {
                progress.InventoryStepDone.Add(key);
            }
        }

        if (armorDirty)
        {
            progress.InventoryArmorPieceDone.Clear();
            foreach (string key in armorPieceKeys)
            {
                progress.InventoryArmorPieceDone.Add(key);
            }
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

    public void MigrateLegacyProgressIfNeeded()
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

internal static class CharacterScope
{
    public static ulong CurrentContentId =>
        Svc.PlayerState.IsLoaded ? Svc.PlayerState.ContentId : 0;
}
