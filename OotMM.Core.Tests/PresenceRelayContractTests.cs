using OotMM.Core;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

public class PresenceRelayContractTests
{
    private static readonly Seed TestSeed = new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "seed",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = 2,
        PlayerCount = 4,
    };

    private static readonly VerifiedClientIdentity TestClient = new(
        Room: "seed", SettingsHash: "settings", TeamId: "team", PlayerId: 2, PlayerCount: 4);

    private const string PoseBody =
        """{"sourcePlayer":2,"game":"oot","sceneId":82,"roomId":0,"pos":[1,2,3],"yaw":0,"joints":[]}""";

    private const string HitBody =
        """{"sourcePlayer":2,"targetPlayer":3,"damage":8,"yaw":16384,"seq":1}""";

    [Fact]
    public void PlayerStateLine_IsAcceptedByTheRelayAllowlist()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, PoseBody, TestSeed, "Jaco");

        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
    }

    [Fact]
    public void PvpHitLine_IsAcceptedByTheRelayAllowlist()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PvpHit, HitBody, TestSeed, "");

        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
    }

    [Theory]
    [InlineData("targetPlayer", -1)]
    [InlineData("targetPlayer", 0)]
    [InlineData("targetPlayer", 5)]
    [InlineData("seq", -1)]
    [InlineData("seq", 0)]
    [InlineData("damage", -1)]
    [InlineData("damage", 0)]
    [InlineData("damage", 256)]
    [InlineData("damage", 100000)]
    public void PvpHitRejectsInvalidTargetSequenceAndDamage(string field, int value)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(HitBody)!;
        body[field] = value;
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PvpHit, body.ToJsonString(), TestSeed, "");
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
    }

    [Fact]
    public void PvpHitRejectsASpoofedSender()
    {
        var line = """{"type":"OOTMM_PVP_HIT","seedId":"seed","teamId":"team","sourcePlayer":3,"targetPlayer":4,"damage":8,"yaw":0,"seq":1}""";
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line.Replace("\"sourcePlayer\":3", "\"sourcePlayer\":2"), TestClient));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void DeathLinkRejectsNonpositiveSequence(int sequence)
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(PresencePackets.DeathLink,
            $$"""{"sourcePlayer":2,"seq":{{sequence}}}""", TestSeed, "");
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
    }

    [Fact]
    public void PresenceLine_CarriesTheRoomIdentityTheAllowlistRequires()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, PoseBody, TestSeed, "Jaco");

        Assert.Equal("seed", RelayProtocol.ExtractStringField(line, "seedId"));
        Assert.Equal("team", RelayProtocol.ExtractStringField(line, "teamId"));
        Assert.Equal("Jaco", RelayProtocol.ExtractStringField(line, "clientName"));
    }

    [Fact]
    public void PresenceLine_OverridesTheSenderReportedByTheGame()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState,
            """{"sourcePlayer":99,"game":"oot","joints":[]}""",
            TestSeed,
            "Jaco");

        Assert.Equal(2, RelayProtocol.ExtractIntField(line, "sourcePlayer"));
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
    }

    [Theory]
    [InlineData("seedId")]
    [InlineData("teamId")]
    public void PresenceLine_IsRejectedWithoutRoomIdentity(string omitted)
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, PoseBody, TestSeed, "Jaco");
        var stripped = line.Replace($"\"{omitted}\":", "\"x\":");

        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(stripped, TestClient));
    }

    [Fact]
    public void RosterFrame_StartsWithItsTypeKey()
    {
        var roster = GameIpcProtocol.BuildRemotePlayerPoses([PoseBody]);

        Assert.StartsWith("{\"type\":\"remotePlayerPoses\"", roster);
    }

    [Fact]
    public void RosterAndHitEnvelopes_RoundTripThroughTheGameFacingParsers()
    {
        var roster = GameIpcProtocol.BuildRemotePlayerPoses([PoseBody, HitBody]);
        Assert.Contains(PoseBody, roster);

        Assert.True(GameIpcProtocol.TryParseLocalPlayerPose(
            $$"""{"type":"localPlayerPose","pose":{{PoseBody}}}""", out var pose));
        Assert.Equal(PoseBody, pose);

        Assert.True(GameIpcProtocol.TryParsePvpHit(
            GameIpcProtocol.BuildPvpHit(HitBody), out var hit));
        Assert.Equal(HitBody, hit);
    }
}
