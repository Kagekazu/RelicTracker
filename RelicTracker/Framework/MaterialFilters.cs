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

        string trimmed = name.Trim();
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

    public static bool IsTrackableCurrency(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        return !trimmed.Contains("calculations", StringComparison.OrdinalIgnoreCase)
               && !trimmed.Contains("Crystal Sand", StringComparison.OrdinalIgnoreCase);
    }
}
