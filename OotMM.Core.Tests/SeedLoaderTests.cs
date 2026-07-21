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

    /// <summary>Re-serializes the valid fixture with one top-level field changed
    /// or removed, for loader-rejection cases.</summary>
    private static string MutateValid(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(ValidJson())!.AsObject();
        mutate(root);
        return root.ToJsonString();
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

        // Omitted owner/source default to the seed's own playerId, not 1.
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

        Assert.Equal([64u, 0x80000040u], seed.EntrancesSong);
        Assert.Equal([0x80000005u], seed.DungeonWarps);
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
    public void MalformedJson_Rejected()
    {
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson("not json"));
        Assert.Throws<SeedLoadException>(() => SeedLoader.LoadFromJson("[1,2,3]"));
    }
}
