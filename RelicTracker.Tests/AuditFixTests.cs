using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RelicTracker.Framework;
using Xunit;

namespace RelicTracker.Tests;

public class AuditFixTests
{
    private static readonly string[] ToolJobs = ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH"];

    [Fact]
    public void AtmaCountsEachDifferentItemOnlyUpToItsShare()
    {
        var line = WeaponLine();
        var atmaIds = Enumerable.Range(100, 12).Select(id => (uint)id).ToList();
        var sheet = Sheet("ARR", new ExpansionMaterialRow
        {
            Step = "Relic", Material = "Atma", MaterialIds = atmaIds, PerUnit = 12, Jobs = [true, true], Obtain = "content",
        });

        // 24 of one Atma covers only that Atma's share (2 jobs need 1 each), not the whole 24.
        var row = Assert.Single(Build("ARR", sheet, line, id => id == 100 ? 24u : 0u));
        Assert.Equal(24u, row.Need);
        Assert.Equal(2u, row.OwnedInventory);
        Assert.Equal(22u, row.Short);
    }

    [Fact]
    public void CraftedTurnInRemovesItsIngredientsFromTheList()
    {
        var line = ToolLine();
        var sheet = Sheet("DoHDoL",
            ToolRow("Rivets", 20, role: "craft"),
            ToolRow("Iron Sand", 20, craftOf: "Rivets", role: "scrip"),
            ToolRow("Manasilver", 20, craftOf: "Rivets"));

        var none = Build("DoHDoL", sheet, line, _ => 0u);
        Assert.Equal(new uint[] { 20, 20, 20 }, none.Select(row => row.Need));

        var half = Build("DoHDoL", sheet, line, _ => 10u);
        Assert.Equal(10u, half.Single(row => row.Material == "Iron Sand").Need);

        var all = Build("DoHDoL", sheet, line, _ => 20u);
        Assert.Equal(["Rivets"], all.Select(row => row.Material));
    }

    [Fact]
    public void QuestRewardCreditIsCappedAtTheJobsStillNeedingIt()
    {
        var sheet = Sheet("DoHDoL",
            new ExpansionMaterialRow { Step = "Supra", Material = "Seal", Role = "quest", MaterialIds = [900], PerUnit = 1, Jobs = [] },
            new ExpansionMaterialRow { Step = "Supra", Material = "Rock", Role = "covers", CraftOf = "Seal", PerUnit = 5, Jobs = [] });
        var index = ShoppingListBuilder.BuildQuestRewardIndex(sheet);

        Assert.Equal(10u, ShoppingListBuilder.QuestCreditFor("Supra", "Rock", 2, index, id => id == 900 ? 3u : 0u));
        Assert.Equal(0u, ShoppingListBuilder.QuestCreditFor("Supra", "Rock", 0, index, id => id == 900 ? 3u : 0u));
    }

    [Fact]
    public void ShopOverlayRescalesTotalsForOneSlotAndWholeSetRows()
    {
        var apply = typeof(ArmorCostRow).Assembly.GetType("RelicTracker.Framework.ArmorShopResolver")!
            .GetMethod("ApplyMaterial", BindingFlags.NonPublic | BindingFlags.Static)!;

        var head = new ArmorCostRow { Set = "Aug. Law's Order (Head)", Currency = "Plating", CurrencyIds = [32181], PerPiece = 10, SetTotal = 10, AllTotal = 70 };
        apply.Invoke(null, [head, ((uint)32181, (uint)12, "Plating")]);
        Assert.Equal((12, 12, 84), (head.PerPiece, head.SetTotal, head.AllTotal));

        var eurekan = new ArmorCostRow { Set = "Artifact (base)", Currency = "Crystal", CurrencyIds = [1], PerPiece = 50, SetTotal = 250, AllTotal = 3750 };
        apply.Invoke(null, [eurekan, ((uint)1, (uint)60, "Crystal")]);
        Assert.Equal((60, 300, 4500), (eurekan.PerPiece, eurekan.SetTotal, eurekan.AllTotal));

        var split = new ArmorCostRow { Set = "Elemental +1", Currency = "Crystal", CurrencyIds = [1], PerPiece = 50, SetTotal = 190, AllTotal = 1330 };
        apply.Invoke(null, [split, ((uint)1, (uint)100, "Crystal")]);
        Assert.Equal((50, 190, 1330), (split.PerPiece, split.SetTotal, split.AllTotal));
    }

    [Fact]
    public void EveryBundledArmorCostLinksToATierAndAddsUpToItsTotal()
    {
        var catalog = Catalog();
        var costs = JsonSerializer.Deserialize<Dictionary<string, List<ArmorCostRow>>>(File.ReadAllText(RepoFile("data", "extracted", "armor_costs.json")))!;
        foreach ((string expansion, var rows) in costs)
        {
            foreach (var row in rows)
            {
                Assert.True(
                    ArmorCostCalculator.TryResolveCostTarget(expansion, row.Set, catalog, out _, out var set, out int tierIndex, out int? slot),
                    $"{expansion} {row.Set} has no armour tier");
                int pieces = set.Tiers[tierIndex].Pieces;
                uint total = slot is not null
                    ? (uint)(row.PerPiece * pieces / ArmorCostCalculator.PiecesPerSet)
                    : (uint)Enumerable.Range(0, pieces).Sum(piece => ArmorCostCalculator.PieceCost(row, piece % ArmorCostCalculator.PiecesPerSet));
                Assert.True(total == (uint)row.AllTotal, $"{expansion} {row.Set}: pieces add up to {total}, row says {row.AllTotal}");
            }
        }
    }

    [Fact]
    public void OwningPhantomVisionCompletesTheArcanautsPieceAndItsCost()
    {
        var catalog = Catalog();
        var line = catalog.ArmorLines.Single(armor => armor.Expansion == "DT");
        var vision = line.Sets.Single(set => set.Name == ArmorCostCalculator.PhantomVisionSet);
        uint ownedId = vision.Tiers[3].PieceIds[7];
        Func<uint, uint> lookup = id => id == ownedId ? 1u : 0u;

        var done = RelicStatusService.BuildArmorPieceDoneKeys(catalog, lookup);
        Assert.Contains("Phantom Vision|7", done);
        Assert.Contains("Phantom Vision +3|7", done);
        Assert.All(line.Sets.Single(set => set.Name == ArmorCostCalculator.ArcanautsSet).Tiers,
            tier => Assert.Contains($"{tier.CollectType}|7", done));
        Assert.DoesNotContain("Phantom Vision|8", done);

        var fixative = new ArmorCostRow { Set = "Arcanaut's +2", Currency = "X-Fixative", CurrencyIds = [1], PerPiece = 6, SetTotal = 30, AllTotal = 210 };
        Assert.Equal(6u, ArmorCostCalculator.ArmorPieceCredit("DT", fixative, catalog, lookup));
    }

    [Fact]
    public void ArmorPieceCountsCombineEverySource()
    {
        var snapshot = new FfxivCollectSnapshot { Owned = [new() { Order = 3, Type = new() { Name = "Phantom Vision" } }] };
        var ownership = new RelicOwnership(
            snapshot,
            manualArmor: new HashSet<string>(["Phantom Vision|0"], StringComparer.Ordinal),
            inventoryArmor: new HashSet<string>(["Phantom Vision|1"], StringComparer.Ordinal));

        Assert.True(ownership.IsArmorPieceOwned("Phantom Vision", 2));
        Assert.Equal(3, ownership.OwnedPieceCount("Phantom Vision", 35));
    }

    [Fact]
    public void ALaterTierOnFfxivCollectCompletesEarlierTiers()
    {
        var line = new RelicLine { CollectType = "Test Weapons", Jobs = 2, TierCount = 3, JobList = ["PLD", "WAR"] };
        // Order = tier * jobs + slot + 1, so WAR (slot 1) at tier 2 is order 6.
        var ownership = new RelicOwnership(new FfxivCollectSnapshot { Owned = [new() { Order = 6, Type = new() { Name = "Test Weapons" } }] });

        Assert.True(ownership.IsCollectStepDone(line, 1, 0));
        Assert.True(ownership.IsCollectStepDone(line, 1, 2));
        Assert.False(ownership.IsCollectStepDone(line, 0, 0));
    }

    [Fact]
    public void HidingPhyseosDropsOnlyThatLastTier()
    {
        var line = new RelicLine { TierCount = 3, Steps = ["Base", "Anemos", "Physeos"] };
        Assert.Equal(2, line.EffectiveTierCount(hidePhyseos: true));
        Assert.Equal(3, line.EffectiveTierCount(hidePhyseos: false));
        Assert.Equal(2, new RelicLine { TierCount = 2, Steps = ["Base", "Anemos"] }.EffectiveTierCount(hidePhyseos: true));
    }

    private static List<ShoppingMaterialRow> Build(string expansion, ExpansionSheet sheet, RelicLine line, Func<uint, uint> owned)
    {
        var catalog = new RelicCatalog();
        Set(catalog, nameof(RelicCatalog.Lines), new List<RelicLine> { line });
        var ownership = new RelicOwnership(FfxivCollectSnapshot.Empty);
        var ids = sheet.Materials
            .Where(row => row.MaterialIds.Count > 0)
            .GroupBy(row => row.Material!)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<uint>)group.First().MaterialIds, StringComparer.OrdinalIgnoreCase);
        return ShoppingListBuilder.Build(expansion, sheet, RelicStatusService.Build(ownership, catalog), ownership, owned,
            new Dictionary<string, string>(), ids);
    }

    private static RelicLine WeaponLine() => new()
    {
        CollectType = "Test Weapons", Expansion = "ARR", Jobs = 2, TierCount = 1, Steps = ["Relic"], JobList = ["PLD", "WAR"], RelicIds = [1, 2],
    };

    private static RelicLine ToolLine() => new()
    {
        CollectType = "Test Tools", Expansion = "DoHDoL", Jobs = 11, TierCount = 1, Steps = ["Skysteel +1"], JobList = [.. ToolJobs],
        RelicIds = [.. Enumerable.Range(1, 11).Select(id => (uint)id)],
    };

    private static ExpansionMaterialRow ToolRow(string material, int per, string? craftOf = null, string? role = null)
    {
        var jobs = new List<bool?>(new bool?[11]) { [1] = true };
        // Item ID 0 keeps the display-name lookup off the game's item sheet. All three rows share it,
        // so the owned count is the product's; only the needs are asserted.
        return new() { Step = "Skysteel +1", Material = material, PerUnit = per, Jobs = jobs, CraftOf = craftOf, Role = role, MaterialIds = [0], Obtain = "gather" };
    }

    private static ExpansionSheet Sheet(string id, params ExpansionMaterialRow[] rows) => new() { Id = id, Materials = [.. rows] };

    private static RelicCatalog Catalog()
    {
        var catalog = new RelicCatalog();
        var armor = JsonSerializer.Deserialize<List<ArmorLine>>(File.ReadAllText(RepoFile("data", "extracted", "relic_armor.json")))!;
        Set(catalog, nameof(RelicCatalog.ArmorLines), armor);
        return catalog;
    }

    private static void Set(RelicCatalog catalog, string property, object value) =>
        typeof(RelicCatalog).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(catalog, value);

    private static string RepoFile(string a, string b, string c, [CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(self))!, a, b, c);
}
