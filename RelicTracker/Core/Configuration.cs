using Dalamud.Configuration;
using Dalamud.Plugin;

namespace RelicTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized] private bool _pendingPersist;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;

    public string SelectedExpansionId { get; set; } = "ARR";

    /// <summary>When true, only count items on the active character (+ retainers). Otherwise all tracked characters.</summary>
    public bool ActiveCharacterOnly { get; set; }

    public ulong FfxivCollectCharacterId { get; set; }

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
