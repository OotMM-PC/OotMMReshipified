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
            StringSettings = new Dictionary<string, string> { ["rainbowBridge"] = "custom" },
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

        var bridge = report.Goals[0];
        Assert.Equal(("BRIDGE", "Rainbow Bridge"), (bridge.Name, bridge.Title));
        Assert.Equal(2, bridge.Owned);
        Assert.Equal(new GoalPart("Spiritual Stones", 2, 2), Assert.Single(bridge.Parts));
        Assert.Empty(bridge.Needs);
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
            StringSettings = new Dictionary<string, string> { ["moon"] = "custom" },
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["MOON"] = new(2, new Dictionary<string, bool> { ["masksRegular"] = true, ["masksOot"] = true }),
            },
        };
        List<(ItemPlacement, bool)> checks = [(Placement(Game.Oot, "A", "SHARED_MASK_KEATON", owner: 1), true)];
        var report = ProgressReport.Build(seed, checks, new Dictionary<string, int> { ["SHARED_MASK_KEATON"] = 1 });

        var moon = report.Goals.Single(g => g.Name == "MOON");
        Assert.Equal(1, moon.Owned);
        Assert.Equal(2, moon.Parts.Count);
    }

    private static IReadOnlyList<GoalProgress> Goals(
        Dictionary<string, string>? strings = null, Dictionary<string, bool>? bools = null,
        Dictionary<string, int>? have = null, Dictionary<string, Seed.SpecialCond>? conds = null,
        int[]? songEventsMm = null)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            StringSettings = strings ?? [],
            BoolSettings = bools ?? [],
            SpecialConds = conds ?? [],
            SongEventsMm = songEventsMm ?? [],
        };
        return ProgressReport.Build(seed, [], have ?? []).Goals;
    }

    private static GoalProgress Goal(IReadOnlyList<GoalProgress> goals, string name) =>
        goals.Single(g => g.Name == name);

    [Fact]
    public void DefaultModesFollowOg()
    {
        var goals = Goals();

        Assert.Equal(["BRIDGE", "LACS", "MOON"], goals.Select(g => g.Name));
        Assert.Equal([new GoalPart("Medallions", 0, 6)], Goal(goals, "BRIDGE").Needs);
        Assert.Equal([new GoalPart("Spirit and Shadow Medallions", 0, 2)], Goal(goals, "LACS").Needs);
        Assert.Equal(
            [
                new GoalPart("Boss Remains", 0, 4), new GoalPart("Oath to Order on the Clock Tower roof", 0, 1),
                new GoalPart("Ocarina", 0, 1),
            ],
            Goal(goals, "MOON").Needs);
    }

    [Fact]
    public void VanillaMoonNeedsEveryRemainsAndAPlayableRoofSong()
    {
        Dictionary<string, int> have = new()
        {
            ["MM_REMAINS_ODOLWA"] = 1, ["MM_REMAINS_GOHT"] = 1, ["MM_REMAINS_GYORG"] = 1, ["MM_SONG_ORDER"] = 1,
            ["MM_OCARINA"] = 1,
        };
        var moon = Goal(Goals(have: have), "MOON");
        Assert.Equal(new GoalPart("Boss Remains", 3, 4), moon.Needs[0]);
        Assert.False(moon.Satisfied);

        have["MM_REMAINS_TWINMOLD"] = 1;
        Assert.True(Goal(Goals(have: have), "MOON").Satisfied);

        have.Remove("MM_OCARINA");
        Assert.False(Goal(Goals(have: have), "MOON").Satisfied);
    }

    [Fact]
    public void RoofSongNeedsTheOcarinaAndItsButtons()
    {
        Dictionary<string, bool> bools = new() { ["sharedOcarina"] = true, ["ocarinaButtonsShuffleMm"] = true };
        Dictionary<string, int> have = new()
        {
            ["MM_SONG_ORDER"] = 1, ["SHARED_OCARINA"] = 1, ["MM_BUTTON_A"] = 1, ["MM_BUTTON_C_UP"] = 1,
        };
        var moon = Goal(Goals(bools: bools, have: have), "MOON");
        Assert.Equal([new GoalPart("Ocarina", 0, 1), new GoalPart("Ocarina buttons for Oath to Order", 2, 4)],
                     moon.Needs.Skip(2));

        bools["fairyOcarinaMm"] = true;
        bools["sharedOcarinaButtons"] = true;
        have["SHARED_BUTTON_C_RIGHT"] = 1;
        var shared = Goal(Goals(bools: bools, have: have), "MOON");
        Assert.Equal([new GoalPart("Ocarina", 1, 1), new GoalPart("Ocarina buttons for Oath to Order", 1, 4)],
                     shared.Needs.Skip(2));
    }

    [Fact]
    public void ShuffledRoofSongIsTheOneNeeded()
    {
        int[] events = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7];
        var moon = Goal(Goals(have: new() { ["MM_SONG_ORDER"] = 1 }, songEventsMm: events), "MOON");
        Assert.Equal(new GoalPart("Bolero of Fire on the Clock Tower roof", 0, 1), moon.Needs[1]);

        Assert.Equal(1, Goal(Goals(have: new() { ["SHARED_SONG_NOTE_TP_FIRE"] = 8 }, songEventsMm: events), "MOON")
                            .Needs[1].Have);
        Assert.Equal(0, Goal(Goals(have: new() { ["MM_SONG_NOTE_TP_FIRE"] = 5, ["SHARED_SONG_NOTE_TP_FIRE"] = 3 },
                                   songEventsMm: events), "MOON").Needs[1].Have);
    }

    [Fact]
    public void GoronRoofSongs()
    {
        int[] events = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15];
        Assert.Equal(new GoalPart("Goron Lullaby on the Clock Tower roof", 1, 1),
                     Goal(Goals(have: new() { ["MM_SONG_GORON_HALF"] = 2 }, songEventsMm: events), "MOON").Needs[1]);
        Assert.Equal(0, Goal(Goals(have: new() { ["MM_SONG_GORON_HALF"] = 1, ["SHARED_SONG_GORON_HALF"] = 1 },
                                   songEventsMm: events), "MOON").Needs[1].Have);

        events[12] = 16;
        Assert.Equal(1, Goal(Goals(have: new() { ["MM_SONG_GORON"] = 1 }, songEventsMm: events), "MOON").Needs[1].Have);
        Assert.Equal(1, Goal(Goals(have: new() { ["MM_SONG_NOTE_GORON"] = 6 }, songEventsMm: events), "MOON")
                            .Needs[1].Have);
        var single = Goals(strings: new() { ["progressiveGoronLullabyMm"] = "single" },
                           have: new() { ["MM_SONG_GORON_HALF"] = 1 }, songEventsMm: events);
        Assert.Equal(0, Goal(single, "MOON").Needs[1].Have);
    }

    [Fact]
    public void OpenAndCustomMoon()
    {
        var open = Goal(Goals(strings: new() { ["moon"] = "open" },
                              have: new() { ["SHARED_SONG_ORDER"] = 1, ["MM_OCARINA"] = 1 }),
                        "MOON");
        Assert.True(open.Satisfied);
        Assert.Equal(["Oath to Order on the Clock Tower roof", "Ocarina"], open.Needs.Select(n => n.Name));

        Assert.DoesNotContain(Goals(strings: new() { ["moon"] = "open" }, bools: new() { ["openMoon"] = true }),
                              g => g.Name == "MOON");

        var custom = Goal(Goals(strings: new() { ["moon"] = "custom" },
                                have: new() { ["MM_REMAINS_GOHT"] = 1 },
                                conds: new()
                                {
                                    ["MOON"] = new(1, new Dictionary<string, bool> { ["remains"] = true }),
                                }),
                          "MOON");
        Assert.Equal((1, 1), (custom.Owned, custom.Threshold));
        Assert.False(custom.Satisfied);
        Assert.Equal(["Oath to Order on the Clock Tower roof", "Ocarina"], custom.Needs.Select(n => n.Name));
    }

    [Fact]
    public void BridgeModes()
    {
        var vanilla = Goal(Goals(strings: new() { ["rainbowBridge"] = "vanilla" },
                                 have: new()
                                 {
                                     ["OOT_MEDALLION_SPIRIT"] = 1, ["OOT_MEDALLION_SHADOW"] = 1,
                                     ["SHARED_ARROW_LIGHT"] = 1,
                                 }),
                           "BRIDGE");
        Assert.Equal([new GoalPart("Spirit and Shadow Medallions", 2, 2), new GoalPart("Light Arrows", 1, 1)],
                     vanilla.Needs);
        Assert.True(vanilla.Satisfied);

        Assert.DoesNotContain(Goals(strings: new() { ["rainbowBridge"] = "open" }), g => g.Name == "BRIDGE");
    }

    [Fact]
    public void StaleSpecialConditionsAreIgnoredUnlessCustom()
    {
        Dictionary<string, Seed.SpecialCond> conds = new()
        {
            ["BRIDGE"] = new(3, new Dictionary<string, bool> { ["stones"] = true }),
            ["MAJORA"] = new(3, new Dictionary<string, bool> { ["remains"] = true }),
        };
        var goals = Goals(conds: conds);

        Assert.Empty(Goal(goals, "BRIDGE").Parts);
        Assert.DoesNotContain(goals, g => g.Name == "MAJORA");
    }

    [Fact]
    public void GanonBossKeyModes()
    {
        Assert.DoesNotContain(Goals(), g => g.Name == "GANON_BK");

        var anywhere = Goal(Goals(strings: new() { ["ganonBossKey"] = "anywhere" }), "GANON_BK");
        Assert.Equal([new GoalPart("Ganon's Boss Key", 0, 1)], anywhere.Needs);
        var vanilla = Goal(Goals(strings: new() { ["ganonBossKey"] = "vanilla" },
                                 have: new() { ["OOT_BOSS_KEY_GANON"] = 1 }), "GANON_BK");
        Assert.True(vanilla.Satisfied);

        var custom = Goal(Goals(strings: new() { ["ganonBossKey"] = "custom" },
                                conds: new()
                                {
                                    ["GANON_BK"] = new(2, new Dictionary<string, bool> { ["stones"] = true }),
                                }),
                          "GANON_BK");
        Assert.Empty(custom.Needs);
        Assert.Equal(2, custom.Threshold);
    }

    [Fact]
    public void SoulsAreNeededWhenNpcSoulsAreShuffled()
    {
        var goals = Goals(bools: new() { ["soulsNpcOot"] = true, ["soulsNpcMm"] = true },
                          have: new() { ["OOT_SOUL_NPC_ZELDA"] = 1 });

        Assert.Equal(["Spirit and Shadow Medallions", "Soul of Sheik", "Soul of Zelda"],
                     Goal(goals, "LACS").Needs.Select(n => n.Name));
        Assert.Equal(1, Goal(goals, "LACS").Needs[2].Have);
        var majora = Goal(goals, "MAJORA");
        Assert.Equal(0, majora.Threshold);
        Assert.Equal("Soul of Moon Children", Assert.Single(majora.Needs).Name);
    }

    [Fact]
    public void TriforceAndSingleGameSeedsDropRows()
    {
        var triforce = Goals(strings: new()
                             {
                                 ["goal"] = "triforce", ["ganonBossKey"] = "anywhere", ["majoraChild"] = "custom",
                             },
                             conds: new() { ["MAJORA"] = new(1, new Dictionary<string, bool> { ["remains"] = true }) });
        Assert.Equal(["TRIFORCE", "BRIDGE", "LACS", "MOON"], triforce.Select(g => g.Name));

        Assert.Equal(["MOON"], Goals(strings: new() { ["games"] = "mm" }).Select(g => g.Name));
        Assert.Equal(["BRIDGE", "LACS"], Goals(strings: new() { ["games"] = "oot" }).Select(g => g.Name));
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

    [Fact]
    public void FoundListsReceivedMajorItemsWithWhoFoundThemAndWhere()
    {
        static ItemPlacement Remote(Game game, string checkId, string itemId, ushort finder) =>
            new(new CheckRef(game, checkId, "", [], null, "", null),
                new ItemRef(itemId.StartsWith("MM_") ? Game.Mm : Game.Oot, itemId, itemId + " name"),
                null, 1, finder, false);

        var pot = new ItemPlacement(new CheckRef(Game.Oot, "OOT_POT", "OOT Kakariko Pot", [], null, "", null),
                                    new ItemRef(Game.Oot, "OOT_HOOKSHOT", "Progressive Hookshot (OoT)"),
                                    null, 1, 1, false);
        var grotto = new ItemPlacement(new CheckRef(Game.Mm, "MM_GROTTO", "MM Swamp Grotto", [], null, "", null),
                                       new ItemRef(Game.Mm, "MM_RUPEE_RED", "Red Rupee"), null, 1, 1, false);
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 1,
            PlayerCount = 3,
            Placements = [pot, grotto],
        };
        List<(ItemPlacement, bool)> checks =
        [
            (pot, true),
            (grotto, true),
            (Placement(Game.Oot, "ARROWS", "OOT_ARROWS_10", owner: 1), true),
            (Placement(Game.Oot, "TOKEN", "OOT_GS_TOKEN", owner: 1), true),
            (Placement(Game.Oot, "NOTE", "OOT_SONG_NOTE_TIME", owner: 1), true),
            (Placement(Game.Oot, "BOW", "OOT_BOW", owner: 1), false),
            (Placement(Game.Oot, "THEIRS", "OOT_HAMMER", owner: 2), true),
        ];
        List<(ItemPlacement, bool)> foreign =
        [
            (Remote(Game.Mm, "MM_GROTTO", "MM_MASK_GORON", 3), true),
            (Remote(Game.Oot, "OOT_UNKNOWN", "SHARED_SONG_EPONA", 2), true),
            (Remote(Game.Oot, "OOT_LATER", "OOT_LENS", 2), false),
        ];

        var report = ProgressReport.Build(seed, checks, [], foreign);

        static string Row(FoundItem f) => $"{f.ItemId}|{f.Group}|{f.CheckName}|{f.Finder}";
        Assert.Equal(["OOT_HOOKSHOT|Items|OOT Kakariko Pot|1", "SHARED_SONG_EPONA||OOT_UNKNOWN|2"],
                     report.Found(Game.Oot).Select(Row));
        Assert.Equal(["MM_MASK_GORON|Masks|MM Swamp Grotto|3", "SHARED_SONG_EPONA||OOT_UNKNOWN|2"],
                     report.Found(Game.Mm).Select(Row));
        Assert.Equal("Progressive Hookshot (OoT)", report.Found(Game.Oot)[0].ItemName);
        Assert.Equal(1, report.PlayerId);
    }
}
