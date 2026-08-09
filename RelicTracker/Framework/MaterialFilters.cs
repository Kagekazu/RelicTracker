namespace RelicTracker.Framework;

internal static class MaterialFilters
{
    private static readonly HashSet<string> NonItemLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Crafters",
        "Fisher",
        "Miner & Botanist",
        "Cosmic",
        "Stellar",
        "Hyper",
        "Select Material",
        "You just do Cosmic Exploration."
    };

    public static bool IsTrackableMaterial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        if (NonItemLabels.Contains(trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith("First ", StringComparison.Ordinal)
            || trimmed.Contains("assume the maximum", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
