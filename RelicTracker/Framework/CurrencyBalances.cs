using FFXIVClientStructs.FFXIV.Client.Game;

namespace RelicTracker.Framework;

internal static class CurrencyBalances
{
  private static readonly string[] AlliedSealItems =
  [
    "Allied Seal of Casting",
    "Allied Seal of Aiming",
    "Allied Seal of Scouting",
    "Allied Seal of Slaying",
    "Allied Seal of Fending",
    "Allied Seal of Healing",
    "Allied Seal of Gathering",
    "Allied Seal of Crafting",
  ];

  public static bool IsTrackable(string? currencyName)
  {
    if (string.IsNullOrWhiteSpace(currencyName))
    {
      return false;
    }

    return currencyName.Trim() switch
    {
      "Gil" => true,
      "Poetics" => true,
      "Company Seals" => true,
      "Allied Seals" => true,
      "Purple Crafter Scrips" => true,
      "Purple Gatherer Scrips" => true,
      "Orange Crafter Scrips" => true,
      "Orange Gatherer Scrips" => true,
      "Skybuilder's Scrips" => true,
      _ => false,
    };
  }

  public static uint GetOwned(string? currencyName, ItemResolver itemResolver, Func<uint, uint> itemLookup)
  {
    if (string.IsNullOrWhiteSpace(currencyName))
    {
      return 0;
    }

    return currencyName.Trim() switch
    {
      "Gil" => GetGil(),
      "Poetics" => GetTomestoneOwned(itemResolver, "Allagan Tomestone of Poetics", itemLookup),
      "Company Seals" => GetCompanySeals(),
      "Allied Seals" => GetAlliedSeals(),
      "Purple Crafter Scrips" => GetCurrencyOwned(itemResolver, "Purple Crafters' Scrip", itemLookup, specialId: 2),
      "Purple Gatherer Scrips" => GetCurrencyOwned(itemResolver, "Purple Gatherers' Scrip", itemLookup, specialId: 4),
      "Orange Crafter Scrips" => GetCurrencyOwned(itemResolver, "Orange Crafters' Scrip", itemLookup, specialId: 6),
      "Orange Gatherer Scrips" => GetCurrencyOwned(itemResolver, "Orange Gatherers' Scrip", itemLookup, specialId: 7),
      "Skybuilder's Scrips" => GetCurrencyOwned(itemResolver, "Skybuilders' Scrip", itemLookup),
      _ => 0,
    };
  }

  public static uint CalculateNeeded(
    string expansionId,
    uint totalPerUnit,
    ExpansionSheet sheet,
    RelicProgressTracker progress)
  {
    if (totalPerUnit == 0)
    {
      return 0;
    }

    if (!progress.UsesCollectProgress)
    {
      return totalPerUnit;
    }

    var progressRows = RelicProgressTracker.GetProgressRows(sheet);
    if (progressRows.Count == 0)
    {
      return totalPerUnit;
    }

    var totalCells = 0;
    var incompleteCells = 0;
    foreach (var row in progressRows)
    {
      for (var jobIndex = 0; jobIndex < row.Jobs.Count; jobIndex++)
      {
        if (!RelicProgressTracker.IsApplicable(row.Jobs[jobIndex]))
        {
          continue;
        }

        totalCells++;
        if (!progress.IsComplete(expansionId, row.Step, row.Label, jobIndex, row.Jobs))
        {
          incompleteCells++;
        }
      }
    }

    if (incompleteCells == 0)
    {
      return 0;
    }

    if (totalCells == 0)
    {
      return totalPerUnit;
    }

    return (uint)Math.Max(1, Math.Round(totalPerUnit * (double)incompleteCells / totalCells));
  }

    private static unsafe uint GetGil()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0u : inventory->GetGil();
    }

  private static unsafe uint GetCompanySeals()
  {
    var inventory = InventoryManager.Instance();
    if (inventory == null)
    {
      return 0;
    }

    uint total = 0;
    for (byte grandCompanyId = 1; grandCompanyId <= 3; grandCompanyId++)
    {
      total += inventory->GetCompanySeals(grandCompanyId);
    }

    return total;
  }

    private static unsafe uint GetAlliedSeals()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0u : inventory->GetAlliedSeals();
    }

  private static unsafe uint GetTomestoneOwned(
    ItemResolver itemResolver,
    string itemName,
    Func<uint, uint> itemLookup)
  {
    var itemId = itemResolver.ResolveItemIds(itemName).FirstOrDefault();
        var inventory = InventoryManager.Instance();
        if (itemId == 0 || inventory == null)
        {
            return SumItems(itemResolver, itemLookup, itemName);
        }

        return inventory->GetTomestoneCount(itemId);
  }

  private static unsafe uint GetCurrencyOwned(
    ItemResolver itemResolver,
    string itemName,
    Func<uint, uint> itemLookup,
    byte? specialId = null)
  {
    var currency = CurrencyManager.Instance();
    if (currency != null)
    {
      var itemId = specialId is byte id
        ? currency->GetItemIdBySpecialId(id)
        : itemResolver.ResolveItemIds(itemName).FirstOrDefault();
      if (itemId > 0)
      {
        return currency->GetItemCount(itemId);
      }
    }

    return SumItems(itemResolver, itemLookup, itemName);
  }

  private static uint SumItems(ItemResolver itemResolver, Func<uint, uint> itemLookup, params string[] names)
  {
    return names.Aggregate(0u, (total, name) =>
      total + itemResolver.ResolveItemIds(name).Aggregate(0u, (sum, itemId) => sum + itemLookup(itemId)));
  }
}
