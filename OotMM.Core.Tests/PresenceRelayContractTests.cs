using OotMM.Core;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

/// <summary>Presence lines must survive the relay's side-channel allowlist. Nothing else covers
/// that contract end to end, and a violation only shows up as dropped frames in a live session.
/// </summary>
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

    [Fact]
    public void PresenceLine_CarriesTheRoomIdentityTheAllowlistRequires()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, PoseBody, TestSeed, "Jaco");

        Assert.Equal("seed", RelayProtocol.ExtractStringField(line, "seedId"));
        Assert.Equal("team", RelayProtocol.ExtractStringField(line, "teamId"));
        Assert.Equal("Jaco", RelayProtocol.ExtractStringField(line, "clientName"));
    }

    /// <summary>A game reporting the wrong sender must not be able to get a frame dropped.</summary>
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

    /// <summary>The bug this suite exists for: a pose carrying only the fields the game knows
    /// about is dropped by the relay, so the stamping above is load-bearing rather than cosmetic.
    /// </summary>
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

    /// <summary>The game matches this frame by prefix so it can defer parsing off its render
    /// path; moving the type key would silently disable presence.</summary>
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
