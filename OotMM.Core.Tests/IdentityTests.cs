using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class IdentityTests
{
    [Fact]
    public void Sha256Hex_MatchesKnownVectors()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Identity.Sha256Hex(""));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Identity.Sha256Hex("abc"));
    }

    [Fact]
    public void KeyFormats_MatchCpp()
    {
        Assert.Equal("oot:OOT_CHECK", Identity.CheckKey(Game.Oot, "OOT_CHECK"));
        Assert.Equal("mm:MM_CHECK", Identity.CheckKey(Game.Mm, "MM_CHECK"));
        Assert.Equal("team|seed", Identity.RewardNamespace("team", "seed"));
    }

    [Fact]
    public void BuildRewardIdentity_MatchesRealSessionValue()
    {
        var roomId = Identity.RewardNamespace(
            "c8fb8f94a21553cbba544da60ca9d535",
            "0981ca8934f81ded6c59dfae95bd82702e639be8d806a58c10690226c9e9ce4efd346cc3ba58b497060c7a5fec260395");
        var identity = Identity.BuildRewardIdentity(
            roomId, sourcePlayer: 2, Game.Oot, "OOT_DEATH_MOUNTAIN_TRAIL_GROTTO_GRASS_4",
            rewardIndex: 0, targetPlayer: 2, Game.Oot);

        Assert.Equal("0089e2c4bfbf6d64006e089fe4b55d2e", identity.ToHex());
    }

    [Fact]
    public void RewardIdentity_HexRoundTripsAndOrders()
    {
        var identity = new RewardIdentity(0x0089e2c4bfbf6d64ul, 0x006e089fe4b55d2eul);
        Assert.Equal(identity, RewardIdentity.FromHex(identity.ToHex()));
        Assert.Null(RewardIdentity.FromHex("short"));
        Assert.Null(RewardIdentity.FromHex("zz89e2c4bfbf6d64006e089fe4b55d2e"));

        Assert.True(new RewardIdentity(1, 0).CompareTo(new RewardIdentity(2, 0)) < 0);
        Assert.True(new RewardIdentity(1, 5).CompareTo(new RewardIdentity(1, 6)) < 0);
    }

    [Fact]
    public void SeedManifestHash_IsOrderInvariantButContentSensitive()
    {
        var a = MakePlacement(Game.Oot, "OOT_A", Game.Mm, "MM_X", owner: 1);
        var b = MakePlacement(Game.Mm, "MM_B", Game.Oot, "OOT_Y", owner: 2);
        var c = MakePlacement(Game.Oot, "OOT_C", Game.Oot, "OOT_Z", owner: 1);

        var hash = Identity.SeedManifestHash(MakeSeed([a, b, c]));
        Assert.Equal(hash, Identity.SeedManifestHash(MakeSeed([c, a, b])));

        var mutated = MakePlacement(Game.Oot, "OOT_A", Game.Mm, "MM_X", owner: 2);
        Assert.NotEqual(hash, Identity.SeedManifestHash(MakeSeed([mutated, b, c])));
    }

    private static ItemPlacement MakePlacement(
        Game checkGame, string checkId, Game itemGame, string itemId, ushort owner) =>
        new(new CheckRef(checkGame, checkId, "", [], null, "", null),
            new ItemRef(itemGame, itemId, itemId + " name"),
            null, owner, owner, false);

    private static Seed MakeSeed(IReadOnlyList<ItemPlacement> placements) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "TESTSEED",
        SettingsHash = "hash",
        TeamId = "team",
        PlayerId = 2,
        PlayerCount = 2,
        Placements = placements,
    };
}
