using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RelicTracker.Framework;
using Xunit;

namespace RelicTracker.Tests;

public class RelicLogicTests
{
    private static readonly string[] LucisJobs = ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH"];

    [Fact]
    public void ProgressKeysKeepTheirSavedFormat()
    {
        // Changing these strings makes every player's saved progress unreadable.
        Assert.Equal("Zodiac|PLD|2", ProgressKeys.Step("Zodiac", "PLD", 2));
        Assert.Equal("Bozjan Armor|7", ProgressKeys.ArmorPiece("Bozjan Armor", 7));
    }

    [Fact]
    public void BundledArmorCostsGiveSanePerPieceCosts()
    {
        var costs = JsonSerializer.Deserialize<Dictionary<string, List<ArmorCostRow>>>(
            File.ReadAllText(RepoFile("data", "extracted", "armor_costs.json")))!;
        Assert.NotEmpty(costs);
        foreach (var row in costs.Values.SelectMany(rows => rows))
        {
            var perSlot = Enumerable.Range(0, ArmorCostCalculator.PiecesPerSet)
                .Select(slot => ArmorCostCalculator.PieceCost(row, slot))
                .ToArray();
            Assert.All(perSlot, cost => Assert.InRange(cost, 1u, (uint)row.SetTotal));
            if (row.SetTotal != row.PerPiece)
            {
                Assert.Equal((uint)row.SetTotal, (uint)perSlot.Sum(cost => cost));
            }
        }
    }

    [Fact]
    public void SingleSlotArmorRowCostsItsOwnPrice()
    {
        var row = new ArmorCostRow { Set = "Aug. Law's Order (Head)", PerPiece = 10, SetTotal = 10 };
        Assert.Equal(10u, ArmorCostCalculator.PieceCost(row, 0));
        Assert.False(ArmorCostCalculator.HasSplitSlotCost(row));
    }

    [Fact]
    public void SplitArmorRowChargesBodyAndLegsMore()
    {
        var row = new ArmorCostRow { Set = "Elemental +1", PerPiece = 50, SetTotal = 190 };
        Assert.Equal(new uint[] { 30, 50, 30, 50, 30 },
            Enumerable.Range(0, 5).Select(slot => ArmorCostCalculator.PieceCost(row, slot)));
    }

    [Theory]
    // jobs flags: 8 crafters, MIN, BTN, FSH
    [InlineData("blanket", 10, false, true)]
    [InlineData("blanket", 10, true, false)]
    [InlineData("blanket", 3, true, true)]
    [InlineData("fisher", 10, true, true)]
    [InlineData("fisher", 3, true, false)]
    [InlineData("miner", 8, true, true)]
    [InlineData("miner", 9, true, false)]
    [InlineData("miner", 10, false, false)]
    public void ToolMaterialRowsReachTheRightJobs(string row, int slot, bool stepHasFisherSection, bool expected)
    {
        var jobs = new bool?[11];
        if (row == "fisher")
        {
            jobs[10] = true;
        }
        else if (row == "miner")
        {
            jobs[8] = true;
        }

        Assert.Equal(expected, ShoppingListBuilder.ToolMaterialAppliesToSlot(jobs, slot, stepHasFisherSection));
    }

    [Fact]
    public void LucisFarmsOnlyThePreferredFirstToolPerCategory()
    {
        var line = LucisLine();
        var skipped = LucisToolShortcut.SkippedSlots(line, Owning(), new Dictionary<string, string> { ["crafter"] = "CUL" });

        Assert.DoesNotContain(7, skipped);
        Assert.Equal(7, skipped.Count(slot => slot < 8));
        Assert.Equal(2, skipped.Count(slot => slot >= 8));
    }

    [Fact]
    public void LucisSkipsEveryCrafterOnceOneHasItsLucisTool()
    {
        var line = LucisLine();
        var ownership = Owning(ProgressKeys.Step(line.CollectType, "ALC", line.TierCount - 1));

        var skipped = LucisToolShortcut.SkippedSlots(line, ownership, null);

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 7 }, skipped.Where(slot => slot < 8).Order());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 7 }, LucisToolShortcut.SalvagerSlots(line, ownership).Where(slot => slot < 8));
    }

    [Fact]
    public void OwningALaterRelicCompletesEveryEarlierStep()
    {
        var line = new RelicLine
        {
            CollectType = "Test Weapons",
            Jobs = 2,
            TierCount = 3,
            JobList = ["PLD", "WAR"],
            RelicIds = [1, 2, 3, 4, 5, 6],
        };
        var catalog = new RelicCatalog();
        typeof(RelicCatalog).GetProperty(nameof(RelicCatalog.Lines), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(catalog, new List<RelicLine> { line });

        var done = RelicStatusService.BuildStepDoneKeys(catalog, itemId => itemId == 6 ? 1u : 0u);

        Assert.Equal(
            new[] { "Test Weapons|WAR|0", "Test Weapons|WAR|1", "Test Weapons|WAR|2" },
            done.Order());
    }

    private static RelicLine LucisLine() => new()
    {
        CollectType = LucisToolShortcut.CollectType,
        Jobs = LucisJobs.Length,
        TierCount = 3,
        Steps = ["Mastercraft", "Supra", "Lucis"],
        JobList = [.. LucisJobs],
    };

    private static RelicOwnership Owning(params string[] stepKeys) =>
        new(FfxivCollectSnapshot.Empty, inventoryDone: new HashSet<string>(stepKeys, StringComparer.Ordinal));

    private static string RepoFile(string a, string b, string c, [CallerFilePath] string self = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(self))!, a, b, c);
}
