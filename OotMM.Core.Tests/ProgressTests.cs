using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class ProgressTests
{
    private static ItemPlacement Placement(
        Game checkGame, string checkId, string itemId, ushort owner, bool major = false, uint? scene = null) =>
        new(new CheckRef(checkGame, checkId, checkId, [], scene, "", null),
            new ItemRef(itemId.StartsWith("MM_") ? Game.Mm : Game.Oot, itemId, itemId + " name"),
            null, owner, owner, major);

    private static ProgressReport BuildReport(
        out List<(ItemPlacement, bool)> checks, int bridgeCount = 2)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 1,
            PlayerCount = 2,
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["BRIDGE"] = new(bridgeCount, new Dictionary<string, bool> { ["stones"] = true }),
            },
        };

        checks =
        [
            (Placement(Game.Oot, "KEY1", "OOT_SMALL_KEY_FOREST", owner: 1, scene: 0x03), true),
            (Placement(Game.Oot, "OOT_FOREST_TEMPLE_KEY2", "OOT_SMALL_KEY_FOREST", owner: 1), false),
            (Placement(Game.Oot, "BK", "OOT_BOSS_KEY_FOREST", owner: 1), false),
            (Placement(Game.Oot, "MAP", "OOT_MAP_FOREST", owner: 1), true),
            (Placement(Game.Oot, "STONE1", "OOT_STONE_EMERALD", owner: 1), true),
            (Placement(Game.Oot, "STONE2", "OOT_STONE_RUBY", owner: 1), true),
            (Placement(Game.Oot, "SONG", "SHARED_SONG_EPONA", owner: 1), false),
            (Placement(Game.Oot, "HOOK", "OOT_HOOKSHOT", owner: 1, major: true), true),
            (Placement(Game.Mm, "SOUL", "MM_SOUL_BOSS_ODOLWA", owner: 1), false),
            (Placement(Game.Mm, "FAIRY", "MM_STRAY_FAIRY_WF", owner: 1), true),
            (Placement(Game.Mm, "THEIRS", "MM_MASK_BUNNY", owner: 2), true),
        ];

        var counts = new Dictionary<string, int>();
        foreach (var (placement, collected) in checks)
        {
            if (collected && placement.OwnerPlayer == 1)
            {
                counts[placement.Item.Id] = counts.GetValueOrDefault(placement.Item.Id) + 1;
            }
        }

        var localChecks = checks;
        return ProgressReport.Build(seed, localChecks, counts);
    }

    [Fact]
    public void ChecksSummary_CountsPerGame()
    {
        var report = BuildReport(out _);
        Assert.Equal(new ChecksSummary(7, 11, 5, 8, 2, 3), report.Checks);
    }

    [Fact]
    public void Dungeons_DeriveFromPoolAndHave()
    {
        var report = BuildReport(out _);
        var forest = Assert.Single(report.DungeonRows, d => d.Name == "Forest Temple");
        Assert.Equal(1, forest.KeysHave);
        Assert.Equal(2, forest.KeysTotal);
        Assert.True(forest.HasBossKey);
        Assert.False(forest.BossKey);
        Assert.True(forest.Map);
        Assert.Equal((1, 2), (forest.ChecksHave, forest.ChecksTotal));

        var woodfall = Assert.Single(report.DungeonRows, d => d.Name == "Woodfall Temple");
        Assert.True(woodfall.HasFairies);
        Assert.Equal(1, woodfall.FairiesHave);

        Assert.DoesNotContain(report.DungeonRows, d => d.Name == "Fire Temple");
    }

    [Fact]
    public void Dungeons_ShowMmCompassesAndSilverRupees()
    {
        var seed = new Seed { Format = Seed.CurrentFormat, GeneratorVersion = Seed.RequiredGeneratorVersion, SeedId = "S" };
        List<(ItemPlacement, bool)> checks =
        [
            (Placement(Game.Mm, "A", "MM_COMPASS_SH", owner: 1), true),
            (Placement(Game.Oot, "B", "OOT_POUCH_SILVER_SHADOW_PIT", owner: 1), true),
            (Placement(Game.Oot, "C", "OOT_POUCH_SILVER_SHADOW_SCYTHE", owner: 1), false),
            (Placement(Game.Oot, "D", "OOT_POUCH_SILVER_SPIRIT_SUN", owner: 1), false),
        ];
        var have = new Dictionary<string, int> { ["MM_COMPASS_SH"] = 1, ["OOT_POUCH_SILVER_SHADOW_PIT"] = 1 };
        var report = ProgressReport.Build(seed, checks, have);

        var snowhead = Assert.Single(report.DungeonRows, d => d.Name == "Snowhead Temple");
        Assert.True(snowhead.HasCompass && snowhead.Compass);
        var shadow = Assert.Single(report.DungeonRows, d => d.Name == "Shadow Temple");
        Assert.Equal((1, 2), (shadow.SilverHave, shadow.SilverTotal));

        have["OOT_RUPEE_MAGICAL"] = 1;
        var spirit = Assert.Single(ProgressReport.Build(seed, checks, have).DungeonRows, d => d.Name == "Spirit Temple");
        Assert.Equal((1, 1), (spirit.SilverHave, spirit.SilverTotal));
    }

    [Fact]
    public void SharedSongs_AppearInBothGames()
    {
        var report = BuildReport(out _);
        Assert.Contains(report.Songs(Game.Oot), s => s.Id == "SHARED_SONG_EPONA");
        Assert.Contains(report.Songs(Game.Mm), s => s.Id == "SHARED_SONG_EPONA");
        Assert.False(report.Songs(Game.Oot)[0].Owned);
    }

    [Fact]
    public void ItemGroupsSoulsAndGoals()
    {
        var report = BuildReport(out _);

        Assert.Equal([ItemGroup.Quest, ItemGroup.Items], report.ItemGroups(Game.Oot).Select(g => g.Group));
        var hookshot = Assert.Single(report.ItemGroups(Game.Oot).Single(g => g.Group == ItemGroup.Items).Items);
        Assert.Equal("OOT_HOOKSHOT name", hookshot.Name);
        Assert.True(hookshot.Complete);
        Assert.Empty(report.ItemGroups(Game.Mm));

        Assert.Single(report.Souls(Game.Mm, SoulSub.Boss));
        Assert.Empty(report.Souls(Game.Oot, SoulSub.Boss));

        var bridge = Assert.Single(report.Goals);
        Assert.Equal(("BRIDGE", "Rainbow Bridge"), (bridge.Name, bridge.Title));
        Assert.Equal(2, bridge.Owned);
        Assert.Equal(new GoalPart("Spiritual Stones", 2, 2), Assert.Single(bridge.Parts));
        Assert.True(bridge.Active);
        Assert.True(bridge.Satisfied);

        Assert.False(BuildReport(out _, bridgeCount: 3).Goals[0].Satisfied);
    }

    [Fact]
    public void SharedSoulsAppearInBothGamesAndStartingItemsCount()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            StartingItems = [new StartingItem(new ItemRef(Game.Oot, "OOT_OCARINA", "Ocarina"), 1)],
        };
        List<(ItemPlacement, bool)> checks =
        [
            (Placement(Game.Oot, "A", "SHARED_SOUL_NPC_MALON", owner: 1), false),
            (Placement(Game.Oot, "B", "OOT_OCARINA", owner: 1), false),
        ];
        var report = ProgressReport.Build(seed, checks, new Dictionary<string, int> { ["OOT_OCARINA"] = 1 });

        Assert.Single(report.Souls(Game.Oot, SoulSub.Npc));
        Assert.Single(report.Souls(Game.Mm, SoulSub.Npc));
        var ocarina = Assert.Single(report.ItemGroups(Game.Oot).Single().Items);
        Assert.Equal((1, 2), (ocarina.Have, ocarina.Total));
    }

    [Fact]
    public void MasksSharedByTwoGoalFamiliesCountOnce()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["MOON"] = new(2, new Dictionary<string, bool> { ["masksRegular"] = true, ["masksOot"] = true }),
            },
        };
        List<(ItemPlacement, bool)> checks = [(Placement(Game.Oot, "A", "SHARED_MASK_KEATON", owner: 1), true)];
        var report = ProgressReport.Build(seed, checks, new Dictionary<string, int> { ["SHARED_MASK_KEATON"] = 1 });

        var moon = Assert.Single(report.Goals);
        Assert.Equal(1, moon.Owned);
        Assert.Equal(2, moon.Parts.Count);
    }

    [Theory]
    [InlineData("OOT_HOOKSHOT", ItemGroup.Items)]
    [InlineData("MM_ARROW_FIRE", ItemGroup.Items)]
    [InlineData("OOT_SWORD_MASTER", ItemGroup.Equipment)]
    [InlineData("OOT_BOMBCHU_BAG", ItemGroup.Equipment)]
    [InlineData("OOT_RUPEE_MAGICAL", ItemGroup.Equipment)]
    [InlineData("MM_STONE_OF_AGONY", ItemGroup.Equipment)]
    [InlineData("OOT_STONE_RUBY", ItemGroup.Quest)]
    [InlineData("SHARED_MASK_KEATON", ItemGroup.Masks)]
    [InlineData("MM_BOTTLED_GOLD_DUST", ItemGroup.Bottles)]
    [InlineData("OOT_CLAIM_CHECK", ItemGroup.Trade)]
    [InlineData("MM_BUTTON_C_UP", ItemGroup.Buttons)]
    [InlineData("MM_CLOCK", ItemGroup.Travel)]
    [InlineData("MM_STRAY_FAIRY_TOWN", ItemGroup.Tokens)]
    public void ItemGrouping_SortsListedItems(string id, ItemGroup expected) =>
        Assert.Equal(expected, ItemGrouping.Classify(id));

    [Theory]
    [InlineData("MM_ARROWS_10")]
    [InlineData("OOT_BOMBCHU_20")]
    [InlineData("OOT_SHIELD_DEKU")]
    [InlineData("MM_RUPEE_GREEN")]
    [InlineData("OOT_TRAP_ICE")]
    [InlineData("OOT_FISHING_POND_CHILD_FISH_8LBS")]
    [InlineData("OOT_SMALL_KEY_FOREST")]
    [InlineData("MM_STRAY_FAIRY_WF")]
    [InlineData("OOT_POUCH_SILVER_DC")]
    [InlineData("SHARED_SOUL_NPC_MALON")]
    public void ItemGrouping_LeavesOutConsumablesAndItemsShownElsewhere(string id) =>
        Assert.Null(ItemGrouping.Classify(id));
}
