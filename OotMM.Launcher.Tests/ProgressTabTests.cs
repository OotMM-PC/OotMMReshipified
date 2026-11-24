using OotMM.Core;
using OotMM.Launcher.ViewModels;

namespace OotMM.Launcher.Tests;

public sealed class ProgressTabTests
{
    private static ProgressTabViewModel Tab(string section)
    {
        var tab = new ProgressTabViewModel();
        tab.LoadSampleData();
        tab.OpenSection(tab.Sections.Single(s => s.Label == section).Key);
        return tab;
    }

    private static string[] Headers(ProgressTabViewModel tab) =>
        [.. tab.Rows.Where(r => r.IsHeader).Select(r => $"{r.Text} {r.CountText}")];

    [Fact]
    public void OverviewNamesGoalsAndBreaksThemDown()
    {
        var tab = Tab("Overview");

        Assert.Equal(["All checks", "Ocarina of Time", "Majora's Mask", "Your items found by other players"],
                     tab.CheckMeters.Select(m => m.Label));
        Assert.Equal("Defeat Ganon and Majora to finish.", tab.GoalText);
        var bridge = tab.Goals.Single(g => g.Name == "Rainbow Bridge");
        Assert.Equal("2 / 4", bridge.CountText);
        Assert.Equal("Any 4 of: Spiritual Stones 2 / 2  ·  Medallions 0 / 2", bridge.Detail);
        Assert.False(bridge.Satisfied);
        var moon = tab.Goals.Single(g => g.Name == "Moon access");
        Assert.Equal("1 / 6", moon.CountText);
        Assert.Equal("Needs: Boss Remains 1 / 4  ·  Oath to Order on the Clock Tower roof ○  ·  Ocarina ○",
                     moon.Detail);
        var majora = tab.Goals.Single(g => g.Name == "Majora fight");
        Assert.Equal("Any 2 of: Boss Remains 1 / 2", majora.Detail);
        Assert.Equal(["Items", "Songs", "Dungeons cleared", "Souls"], tab.Collection.Select(m => m.Label));
    }

    [Fact]
    public void TriforceRowsHaveNoBreakdown()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            StringSettings = new Dictionary<string, string>
            {
                ["goal"] = "triforce", ["games"] = "mm", ["moon"] = "open",
            },
            BoolSettings = new Dictionary<string, bool> { ["openMoon"] = true },
            NumberSettings = new Dictionary<string, double> { ["triforceGoal"] = 20 },
        };
        var tab = new ProgressTabViewModel();
        tab.SetReport(ProgressReport.Build(seed, [], new Dictionary<string, int> { ["SHARED_TRIFORCE"] = 3 }),
                      "triforce", false, false, false);

        var triforce = Assert.Single(tab.Goals);
        Assert.Equal(("Triforce Hunt", "3 / 20", ""), (triforce.Name, triforce.CountText, triforce.Detail));
    }

    [Fact]
    public void ItemsAreGroupedSortedAndFreeOfConsumables()
    {
        var tab = Tab("Items");

        Assert.Equal(["Goal items 2 / 4", "Equipment and upgrades 1 / 3", "Items 1 / 2", "Bottles 1 / 1",
                      "Trade and quest items 0 / 1", "Tokens and fairies 0 / 1", "Rusty keys 1 / 1"],
                     Headers(tab));
        Assert.DoesNotContain(tab.Rows, r => r.Text.Contains("Arrows"));
        var hookshot = tab.Rows.Single(r => r.Text == "Hookshot");
        Assert.Equal(("1 / 2", 1), (hookshot.CountText, hookshot.Level));
        Assert.Contains(tab.Rows, r => r is { Text: "Guard House", Level: 2 });

        tab.GameIndex = 1;
        Assert.Contains(tab.Rows, r => r.Text == "Bunny Hood");
        Assert.DoesNotContain(tab.Rows, r => r.Text == "Hookshot");
    }

    [Fact]
    public void MissingOnlyHidesFinishedRowsAndGroups()
    {
        var tab = Tab("Items");
        tab.MissingOnly = true;

        Assert.DoesNotContain("Bottles 1 / 1", Headers(tab));
        Assert.Contains(tab.Rows, r => r.Text == "Hookshot");
        Assert.DoesNotContain(tab.Rows, r => r.Text == "Master Sword");

        tab.OpenSection(tab.Sections.Single(s => s.Label == "Songs").Key);
        Assert.Equal(["Saria's Song"], tab.Rows.Where(r => !r.IsHeader).Select(r => r.Text));
    }

    [Fact]
    public void FoundSaysWhoFoundEachMajorItemAndWhere()
    {
        var tab = Tab("Found");

        Assert.True(tab.ShowList);
        Assert.False(tab.ShowMissingOnly);
        Assert.Contains(tab.Rows, r => r is { Text: "Hookshot", Detail: "You · C10", Level: 2 });
        Assert.Contains(tab.Rows, r => r is { IsHeader: true, Text: "Songs", CountText: "2" });
        Assert.DoesNotContain(tab.Rows, r => r.Text is "Hookshot" && r.Detail == "You · C11");
        Assert.DoesNotContain(tab.Rows, r => r.Text.Contains("Arrows") || r.Text.Contains("Token"));

        tab.GameIndex = 1;
        Assert.Contains(tab.Rows, r => r is { Text: "Goron Mask", Detail: "Player 2 · F1" });
        Assert.True(tab.ShowFoundFilter);

        tab.FoundFilter = "player 2";
        Assert.Equal(["Masks 1", "Goron Mask"],
                     tab.Rows.Select(r => r.IsHeader ? $"{r.Text} {r.CountText}" : r.Text));
        tab.FoundFilter = "deku";
        Assert.Equal(["Deku Mask"], tab.Rows.Where(r => !r.IsHeader).Select(r => r.Text));
        tab.FoundFilter = "nothing like this";
        Assert.Empty(tab.Rows);
        Assert.Equal("No found items match.", tab.EmptyText);
        tab.FoundFilter = "";

        tab.OpenSection(tab.Sections.Single(s => s.Label == "Items").Key);
        Assert.False(tab.ShowFoundFilter);
        Assert.True(tab.ShowMissingOnly);
        Assert.All(tab.Rows, r => Assert.Equal("", r.Detail));
    }

    [Fact]
    public void SoulsDropThePrefixAndShareAcrossGames()
    {
        var tab = Tab("Souls");
        Assert.Equal(["Enemy souls 1 / 1", "Boss souls 0 / 1", "Character souls 0 / 1"], Headers(tab));
        Assert.Contains(tab.Rows, r => r.Text == "Malon");

        tab.GameIndex = 1;
        Assert.Equal(["Enemy souls 0 / 1", "Character souls 0 / 1"], Headers(tab));
        Assert.Contains(tab.Rows, r => r.Text == "Octoroks");
    }

    [Fact]
    public void DungeonsSayWhatIsFoundInWords()
    {
        var tab = Tab("Dungeons");

        var forest = tab.Dungeons.Single(d => d.Name == "Forest Temple");
        Assert.Equal("2 / 5", forest.ChecksText);
        Assert.Equal(["Small keys 1/2:False", "Boss key:False", "Map:True"], forest.Chips.Select(c => $"{c.Text}:{c.Ok}"));
        var woodfall = tab.Dungeons.Single(d => d.Name == "Woodfall Temple");
        Assert.Equal(["Key ring:True", "Boss key:True", "Fairies 1/2:False"], woodfall.Chips.Select(c => $"{c.Text}:{c.Ok}"));
        Assert.Equal(["Ocarina of Time", "Majora's Mask"], tab.Dungeons.Where(d => d.IsHeader).Select(d => d.Name));
    }
}
