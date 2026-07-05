namespace RelicTracker.Framework;

/// <summary>Per-character relic progress (manual ticks and last Allagan Tools inventory snapshot).</summary>
[Serializable]
public sealed class CharacterProgress
{
    /// <summary>Manual step ticks: collectType|job|tier</summary>
    public HashSet<string> RelicStepDone { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Manual armor piece ticks: collectType|pieceIndex</summary>
    public HashSet<string> ArmorPieceDone { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Last inventory scan for this character: collectType|job|tier (includes replicas).</summary>
    public HashSet<string> InventoryStepDone { get; set; } = new(StringComparer.Ordinal);
}
