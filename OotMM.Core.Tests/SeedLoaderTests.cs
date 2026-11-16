using System.Text.Json;
using System.Text.Json.Nodes;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class SeedLoaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string ValidJson() => File.ReadAllText(FixturePath("valid.v2.ootmm.json"));

    private static string MutateValid(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("single", false)]
    [InlineData("coop", true)]
    [InlineData("multi", true)]
    public void SinglePlayerSeed_IsNetworkedUnlessModeIsSingle(string mode, bool networked)
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root["playerId"] = 1;
            root["playerCount"] = 1;
            root["settings"]!["mode"] = mode;
        }));

        Assert.Equal(networked, seed.IsMultiworld);
    }

    [Fact]
    public void RequiredFlag_IsUnknownUntilTheSeedCarriesIt()
    {
        Assert.All(SeedLoader.LoadFromJson(ValidJson()).Placements, p => Assert.Null(p.Required));

        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            var placements = root["placements"]!.AsArray();
            for (var i = 0; i < placements.Count; i++)
            {
                placements[i]!["woth"] = i == 0;
            }
        }));

        Assert.True(seed.Placements[0].Required);
        Assert.All(seed.Placements.Skip(1), p => Assert.False(p.Required));
    }

    [Fact]
    public void ValidSeed_ParsesIdentityAndScalars()
    {
        var seed = SeedLoader.LoadFromJson(ValidJson());

        Assert.Equal("TESTSEED01", seed.SeedId);
        Assert.Equal("abc123", seed.SettingsHash);
        Assert.Equal(2, seed.PlayerId);
        Assert.Equal(3, seed.PlayerCount);
        Assert.Equal("alpha", seed.TeamId);
        Assert.Equal(7ul, seed.SessionEpoch);
        Assert.Equal(Game.Mm, seed.StartingGame);
        Assert.True(seed.IsMultiworld);
        Assert.True(seed.Cosmetics.GerudoTunic);
        Assert.True(seed.Cosmetics.DefaultHold);
        Assert.False(seed.Cosmetics.MusicCategories);
        Assert.Equal("music.ocm", seed.Cosmetics.MusicCompanionFile);
    }

    [Fact]
    public void ValidSeed_ParsesPlacementsWithDefaultsAndCloak()
    {
        var seed = SeedLoader.LoadFromJson(ValidJson());

        Assert.Equal(2, seed.Placements.Count);

        var mq = seed.Placements[0];
        Assert.Equal(Game.Oot, mq.Check.Game);
        Assert.Equal("OOT_MQ_DEKU_TREE_MAP_CHEST", mq.Check.Id);
        Assert.Equal(["OOT_DEKU_MQ_ALT"], mq.Check.Aliases);
        Assert.Equal(0u, mq.Check.SceneId);
        Assert.Equal(3u, mq.Check.Flag);
        Assert.Equal("MM_SONG_SOARING", mq.Item.Id);
        Assert.NotNull(mq.Cloak);
        Assert.Equal("OOT_RUPEE_GREEN", mq.Cloak!.Id);
        Assert.Equal(1, mq.OwnerPlayer);
        Assert.Equal(2, mq.SourcePlayer);
        Assert.True(mq.Major);

        var postbox = seed.Placements[1];
        Assert.Null(postbox.Cloak);
        Assert.Equal(2, postbox.OwnerPlayer);
        Assert.Equal(2, postbox.SourcePlayer);
        Assert.False(postbox.Major);
    }

    [Fact]
    public void ValidSeed_ForeignEntranceBitSurvivesWarpTables()
    {
        var seed = SeedLoader.LoadFromJson(ValidJson());

        Assert.Equal([64u, 0x80000040u, 1540u, 497u, 1384u, 1524u], seed.EntrancesSong);
        Assert.Equal([0x1000u, 0x80000005u], seed.EntrancesSpawn);
        Assert.True(seed.IsSpawnDestination(Game.Oot, 0x1000u));
        Assert.True(seed.IsSpawnDestination(Game.Mm, 5u));
        Assert.False(seed.IsSpawnDestination(Game.Mm, 0x1000u));
        Assert.Equal(12, seed.DungeonWarps.Count);
        Assert.Equal(0x80008610u, seed.DungeonWarps[8]);
    }

    [Fact]
    public void ValidSeed_ParsesSettingsBucketsWorldFlagsAndSpecialConds()
    {
        var seed = SeedLoader.LoadFromJson(ValidJson());

        Assert.True(seed.GetBoolSetting("openForest"));
        Assert.Equal("open", seed.GetStringSetting("doorOfTime"));
        Assert.Equal(20, seed.GetIntSetting("triforceGoal"));

        Assert.True(seed.WorldFlags["everything"].All);
        Assert.False(seed.WorldFlags["nothing"].All);
        Assert.Empty(seed.WorldFlags["nothing"].Members);
        Assert.Equal(["MEMBER_A", "MEMBER_B"], seed.WorldFlags["explicit"].Members);

        var bridge = seed.SpecialConds["BRIDGE"];
        Assert.Equal(9, bridge.Count);
        Assert.True(bridge.Field("stones"));
        Assert.False(bridge.Field("medallions"));
        Assert.False(bridge.Field("missing"));
    }

    [Fact]
    public void ValidSeed_DerivedQueries()
    {
        var seed = SeedLoader.LoadFromJson(ValidJson());

        Assert.True(seed.NeedsMasterQuest());
        Assert.Equal(Game.Mm, seed.ResolveStartingGame());

        Assert.NotNull(seed.FindPlacement(Game.Oot, "OOT_MQ_DEKU_TREE_MAP_CHEST"));
        Assert.NotNull(seed.FindPlacement(Game.Oot, "Deku Tree MQ Map Chest"));
        Assert.NotNull(seed.FindPlacement(Game.Oot, "OOT_DEKU_MQ_ALT"));
        Assert.Null(seed.FindPlacement(Game.Mm, "OOT_MQ_DEKU_TREE_MAP_CHEST"));
    }

    [Fact]
    public void StartingGame_InferredFromStartingItemsWhenAbsent()
    {
        var allMm = SeedLoader.LoadFromJson(MutateValid(root => root.Remove("startingGame")));
        Assert.Equal(Game.Mm, allMm.ResolveStartingGame());

        var empty = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root.Remove("startingGame");
            root["startingItems"] = new JsonArray();
        }));
        Assert.Equal(Game.Oot, empty.ResolveStartingGame());
    }

    [Fact]
    public void CrossGameSpawn_DoesNotBypassOotFileSetup()
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root["startingGame"] = "oot";
            root["settings"]!["startingAge"] = "child";
            root["entrances"] = new JsonArray
            {
                new JsonObject
                {
                    ["fromGame"] = "oot",
                    ["from"] = "OOT_SPAWN_CHILD",
                    ["fromNativeId"] = 0x00BB,
                    ["toGame"] = "mm",
                    ["to"] = "MM_TERMINA_FIELD_FROM_CLOCK_TOWN_SOUTH",
                    ["toNativeId"] = 0x5460,
                },
            };
        }));

        Assert.Equal(Game.Oot, seed.ResolveStartingGame());
    }

    [Fact]
    public void V1Seed_RejectedWithRegenerateMessage()
    {
        var json = File.ReadAllText(FixturePath("legacy.v1.ootmm.json"));
        var ex = Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(json));
        Assert.Contains("predates v31", ex.Message);
    }

    [Fact]
    public void WrongGeneratorVersion_Rejected()
    {
        var json = MutateValid(root => root["generatorVersion"] = "30.0");
        var ex = Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(json));
        Assert.Contains("v31.1", ex.Message);
    }

    [Fact]
    public void SongEvents_RequireExactCounts()
    {
        var missing = MutateValid(root => root.Remove("songEventsOot"));
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(missing));

        var short17 = MutateValid(root =>
            root["songEventsOot"]!.AsArray().RemoveAt(0));
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(short17));

        var outOfRange = MutateValid(root =>
            root["songEventsMm"]!.AsArray()[0] = 20);
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(outOfRange));
    }

    [Fact]
    public void SessionEpochZero_Rejected()
    {
        var json = MutateValid(root => root["sessionEpoch"] = 0);
        var ex = Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(json));
        Assert.Contains("sessionEpoch", ex.Message);
    }

    [Fact]
    public void SessionEpoch_AcceptsStringForm()
    {
        var json = MutateValid(root => root["sessionEpoch"] = "9007199254740993");
        var seed = SeedLoader.LoadFromJson(json);
        Assert.Equal(9007199254740993ul, seed.SessionEpoch);
    }

    [Fact]
    public void StartingChecks_FollowGeneratorStartingItemRules()
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root["settings"]!["skipZelda"] = true;
            root["settings"]!["shuffleWonderItemsOot"] = "overworld";
            root["settings"]!["gerudoFortress"] = "open";
            root["worldFlags"] = JsonNode.Parse("""{"mmPreActivatedOwls":["swamp","tower"]}""");
        }));

        Assert.True(seed.IsStartingCheck("OOT_ZELDAS_LETTER"));
        Assert.True(seed.IsStartingCheck("OOT_ZELDAS_SONG"));
        Assert.True(seed.IsStartingCheck("OOT_CASTLE_COURTYARD_WONDER_ITEM"));
        Assert.True(seed.IsStartingCheck("OOT_GERUDO_MEMBER_CARD"));
        Assert.True(seed.IsStartingCheck("MM_SOUTHERN_SWAMP_OWL_STATUE"));
        Assert.True(seed.IsStartingCheck("MM_STONE_TOWER_OWL_STATUE"));
        Assert.False(seed.IsStartingCheck("MM_CLOCK_TOWN_OWL_STATUE"));
        Assert.False(seed.IsStartingCheck("OOT_MALON_EGG"));
    }

    [Fact]
    public void SeedName_IsOptional()
    {
        Assert.Equal("", SeedLoader.LoadFromJson(ValidJson()).SeedName);
        Assert.Equal("Juicy Gibdo",
            SeedLoader.LoadFromJson(MutateValid(root => root["seedName"] = "Juicy Gibdo")).SeedName);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FixedChecks_HideUnshuffledFrogsAndLottery(bool shuffled, bool expectedFixed)
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root["settings"]!["shuffleFrogsRupeesOot"] = shuffled;
            root["settings"]!["shuffleLotteryMm"] = shuffled;
        }));
        ItemPlacement Placement(Game game, string id, string name, string item) => new(
            new CheckRef(game, id, name, [], 84, "npc", null), new ItemRef(game, item, item), null, 1, 1, false);

        var frog = Placement(Game.Oot, "OOT_ZORA_RIVER_FROGS_SARIAS_SONG", "OOT Zora River Frogs Sarias Song", "OOT_RUPEE_PURPLE");
        var lottery = Placement(Game.Mm, "MM_LOTTERY_PRIZE_NIGHT_1", "MM Lottery Prize Night 1", "MM_RUPEE_PURPLE");
        var frogGame = Placement(Game.Oot, "OOT_ZORA_RIVER_FROGS_GAME", "OOT Zora River Frogs Game", "OOT_RUPEE_RED");

        Assert.Equal(expectedFixed, FixedChecks.IsFixed(seed, frog));
        Assert.Equal(expectedFixed, FixedChecks.IsFixed(seed, lottery));
        Assert.Equal(!expectedFixed, seed.IsTrackedPlacement(frog));
        Assert.False(FixedChecks.IsFixed(seed, frogGame));
    }

    [Fact]
    public void StartingChecks_EmptyWithoutStartingRules()
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root["settings"]!["skipZelda"] = false;
            root["settings"]!["gerudoFortress"] = "single";
            root["worldFlags"] = JsonNode.Parse("""{"mmPreActivatedOwls":"none"}""");
        }));

        Assert.False(seed.IsStartingCheck("OOT_ZELDAS_LETTER"));
        Assert.False(seed.IsStartingCheck("OOT_GERUDO_MEMBER_CARD"));
        Assert.False(seed.IsStartingCheck("MM_CLOCK_TOWN_OWL_STATUE"));
    }

    [Fact]
    public void MalformedJson_Rejected()
    {
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson("not json"));
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson("[1,2,3]"));
    }

    [Theory]
    [InlineData("entrancesSpawn", "[]")]
    [InlineData("entrancesSpawn", "[1]")]
    [InlineData("entrancesSpawn", "[1,2,3]")]
    [InlineData("entrancesSpawn", "[1,-1]")]
    [InlineData("entrancesSpawn", "[1,4294967296]")]
    [InlineData("entrancesSpawn", "[1,1.5]")]
    [InlineData("entrancesSpawn", "[1,\"2\"]")]
    [InlineData("entrancesSpawn", "null")]
    [InlineData("dungeonWarps", "[1]")]
    [InlineData("bossIds", "[]")]
    [InlineData("bossIds", "[0,1,2,3,4,5,6,7,8,9,10,10]")]
    [InlineData("bossIds", "[0,1,2,3,4,5,6,7,8,9,10,12]")]
    [InlineData("bossIds", "[0,1,2,3,4,5,6,7,8,9,10,-1]")]
    [InlineData("bossIds", "[0,1,2,3,4,5,6,7,8,9,10,11.5]")]
    [InlineData("bossIds", "[0,1,2,3,4,5,6,7,8,9,10,4294967296]")]
    [InlineData("entrancesSong", "[]")]
    [InlineData("entrancesSong", "[1,2,3,4,5]")]
    [InlineData("entrancesOwl", "[1,2,3,4,5,6,7,8,9]")]
    [InlineData("entrancesSong", "[-1]")]
    [InlineData("entrancesOwl", "[4294967296]")]
    public void RejectsMalformedResolvedTable(string key, string value)
    {
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson(
            MutateValid(root => root[key] = JsonNode.Parse(value))));
    }

    [Fact]
    public void AllowsAbsentOptionalResolvedTables()
    {
        var seed = SeedLoader.LoadFromJson(MutateValid(root =>
        {
            root.Remove("entrancesSpawn");
            root.Remove("entrancesSong");
            root.Remove("entrancesOwl");
            root.Remove("dungeonWarps");
            root.Remove("bossIds");
        }));
        Assert.Empty(seed.EntrancesSpawn);
        Assert.Empty(seed.EntrancesSong);
        Assert.Empty(seed.EntrancesOwl);
        Assert.Empty(seed.DungeonWarps);
        Assert.Empty(seed.BossIds);
    }

    [Theory]
    [InlineData(Game.Oot, 0x80000005u)]
    [InlineData(Game.Mm, 0x80000005u)]
    [InlineData((Game)99, 0x1000u)]
    public void RejectsInvalidSpawnGameOrEncodedId(Game game, uint nativeId)
    {
        Assert.False(SeedLoader.LoadFromJson(ValidJson()).IsSpawnDestination(game, nativeId));
    }
}
