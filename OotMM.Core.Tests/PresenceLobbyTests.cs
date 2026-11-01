using OotMM.Core;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class PresenceLobbyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ootmm-lobby-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = [];

    public PresenceLobbyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private static Seed MakeSeed(ushort player, ushort count, string team) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "seed",
        TeamId = team,
        PlayerId = player,
        PlayerCount = count,
    };

    private PresenceLobby Lobby(string name, Seed seed)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return new PresenceLobby(seed, dir, _logs.Add);
    }

    private static string Pose(Seed seed, PresenceLobby lobby) =>
        lobby.Globalize(GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, """{"game":"oot","sceneId":85}""", seed, "name"), false)!;

    [Fact]
    public void SameTeamPlayers_KeepTheirPlayerNumberAsSlot()
    {
        var mine = MakeSeed(1, 3, "a");
        var theirs = MakeSeed(2, 3, "a");
        var local = Lobby("one", mine);
        var line = local.Localize(Pose(theirs, Lobby("two", theirs)));

        Assert.Equal(2, RelayProtocol.ExtractIntField(line!, "sourcePlayer"));
        Assert.Empty(_logs);
    }

    [Fact]
    public void CoopPartnersAndOtherTeams_GetStableExtraSlots()
    {
        var mine = MakeSeed(1, 1, "a");
        var partner = MakeSeed(1, 1, "a");
        var rival = MakeSeed(1, 1, "b");
        var partnerLobby = Lobby("partner", partner);
        var rivalLobby = Lobby("rival", rival);

        var local = Lobby("mine", mine);
        var partnerSlot = RelayProtocol.ExtractIntField(local.Localize(Pose(partner, partnerLobby))!, "sourcePlayer");
        var rivalSlot = RelayProtocol.ExtractIntField(local.Localize(Pose(rival, rivalLobby))!, "sourcePlayer");

        Assert.Equal(2, partnerSlot);
        Assert.Equal(3, rivalSlot);
        Assert.Equal([2, 3], local.RemoteSlots);

        var reopened = Lobby("mine", mine);
        Assert.Equal(local.OwnKey, reopened.OwnKey);
        Assert.Equal(3, RelayProtocol.ExtractIntField(reopened.Localize(Pose(rival, rivalLobby))!, "sourcePlayer"));
    }

    [Fact]
    public void OwnEchoesAndUnkeyedLines_AreDropped()
    {
        var mine = MakeSeed(1, 2, "a");
        var local = Lobby("mine", mine);

        Assert.Null(local.Localize(Pose(mine, local)));
        Assert.Null(local.Localize(GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, """{"game":"oot"}""", MakeSeed(2, 2, "a"), "")));
    }

    [Fact]
    public void TargetedHits_ReachOnlyTheLobbyBehindTheSlot()
    {
        var attackerSeed = MakeSeed(1, 1, "a");
        var victimSeed = MakeSeed(1, 1, "a");
        var bystanderSeed = MakeSeed(1, 1, "b");
        var attacker = Lobby("attacker", attackerSeed);
        var victim = Lobby("victim", victimSeed);
        var bystander = Lobby("bystander", bystanderSeed);

        var victimSlot = RelayProtocol.ExtractIntField(attacker.Localize(Pose(victimSeed, victim))!, "sourcePlayer");
        attacker.Localize(Pose(victimSeed, victim));
        var hit = attacker.Globalize(GameIpcProtocol.BuildRelayPresenceLine(PresencePackets.PvpHit,
            $$"""{"targetPlayer":{{victimSlot}},"damage":8,"seq":5}""", attackerSeed, ""), true)!;

        Assert.Equal(1, RelayProtocol.ExtractIntField(hit, "targetPlayer"));
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(hit,
            new VerifiedClientIdentity("seed", "", "a", 1, 1)));
        var received = victim.Localize(hit);
        Assert.NotNull(received);
        Assert.Equal(1, RelayProtocol.ExtractIntField(received!, "targetPlayer"));
        Assert.Equal(2, RelayProtocol.ExtractIntField(received!, "sourcePlayer"));
        Assert.Null(bystander.Localize(hit));
        Assert.Null(attacker.Globalize(GameIpcProtocol.BuildRelayPresenceLine(PresencePackets.PvpHit,
            """{"targetPlayer":9,"damage":8,"seq":6}""", attackerSeed, ""), true));
    }

    [Fact]
    public void ModelChunks_KeepTheirBase64PayloadVerbatim()
    {
        var mine = MakeSeed(1, 2, "a");
        var theirs = MakeSeed(2, 2, "a");
        var sender = Lobby("sender", theirs);
        var chunk = Models.ModelSyncProtocol.BuildChunk(theirs,
            new Models.ModelChunk(2, "ab", 0, 1, [0xfb, 0xff, 0xbf]));
        var received = Lobby("mine", mine).Localize(sender.Globalize(chunk, false)!);

        Assert.Equal(RelayProtocol.ExtractStringField(chunk, "data"), RelayProtocol.ExtractStringField(received!, "data"));
        Assert.Contains('+', RelayProtocol.ExtractStringField(received!, "data"));
    }
}
