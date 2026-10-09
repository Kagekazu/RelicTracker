using System.Text;

namespace RelicTracker.IPC;

internal readonly record struct KnightshopperListItem(
    uint ItemId,
    uint Quantity);

/// <summary>JSON encoder for Knightshopper's clipboard shopping-list importer.</summary>
/// <remarks>No Knightshopper assembly or IPC required; paste into the matching currency page.</remarks>
internal static class KnightshopperListExport
{
    /// <summary>Creates JSON for Knightshopper's clipboard list importer.</summary>
    /// <remarks>Knightshopper resolves vendor, shop, and subtype when the user imports it.</remarks>
    public static string Encode(
        KnightshopperCurrency currency,
        string name,
        IEnumerable<KnightshopperListItem> items)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(items);
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency));
        }

        name = name.Trim();
        if (name.Length > 100)
        {
            throw new ArgumentException("List names must be at most 100 characters.", nameof(name));
        }

        var rows = new List<object>();
        foreach (var item in items)
        {
            if (rows.Count == 500)
            {
                throw new ArgumentException("Lists must contain at most 500 items.", nameof(items));
            }

            if (item.ItemId == 0 || item.Quantity == 0)
            {
                throw new ArgumentException("Items need a nonzero ID and quantity.", nameof(items));
            }

            rows.Add(new { itemId = item.ItemId, quantity = item.Quantity });
        }

        var json = JsonSerializer.Serialize(new { name, currency = currency.ToString(), items = rows });
        if (json.Length > 65_536 || Encoding.UTF8.GetByteCount(json) > 262_144)
        {
            throw new ArgumentException("The export exceeds Knightshopper's size limits.", nameof(items));
        }

        return json;
    }
}
