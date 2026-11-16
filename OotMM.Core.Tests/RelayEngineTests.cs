using System.Net.Sockets;
using System.Text;
using OotMM.Core;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

public class RelayEngineTests : IDisposable
{
    private readonly string _store =
        Path.Combine(Path.GetTempPath(), "ootmm-relay-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly CancellationTokenSource _stop = new();
    private RelayEngine? _engine;
    private Thread? _thread;

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(5));
        try
        {
            Directory.Delete(_store, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private int StartEngine(RelayRules? rules = null)
    {
        _engine = new RelayEngine(_store, rules ?? new RelayRules()) { Log = _ => { } };
        _thread = new Thread(() => _engine.Run(0, _stop.Token)) { IsBackground = true };
        _thread.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_engine.BoundPort == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.NotEqual(0, _engine.BoundPort);
        return _engine.BoundPort;
    }

    private sealed class Wire(int port) : IDisposable
    {
        private readonly TcpClient _client = new("127.0.0.1", port);
        private readonly StringBuilder _received = new();

        public void Send(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            _client.GetStream().Write(bytes);
        }

        public string? ReceiveWhere(Func<string, bool> match, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var stream = _client.GetStream();
            var chunk = new byte[4096];
            while (DateTime.UtcNow < deadline)
            {
                int newline;
                while ((newline = _received.ToString().IndexOf('\n')) >= 0)
                {
                    var line = _received.ToString(0, newline).TrimEnd('\r');
                    _received.Remove(0, newline + 1);
                    if (match(line))
                    {
                        return line;
                    }
                }

                stream.ReadTimeout = 200;
                try
                {
                    var read = stream.Read(chunk);
                    if (read == 0)
                    {
                        return null;
                    }

                    _received.Append(Encoding.UTF8.GetString(chunk, 0, read));
                }
                catch (IOException)
                {
                }
            }

            return null;
        }

        public string? ReceiveType(string type, int timeoutMs = 3000) =>
            ReceiveWhere(l => RelayProtocol.ExtractStringField(l, "type") == type, timeoutMs);

        public void Dispose() => _client.Dispose();
    }

    private static Seed MakeSeed(ushort playerId, string teamId = "team") => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "protocol-test",
        SettingsHash = "settings",
        TeamId = teamId,
        PlayerId = playerId,
        PlayerCount = 2,
        SessionEpoch = 4,
        Placements =
        [
            new ItemPlacement(new CheckRef(Game.Oot, "CHECK_A", "", [], null, "", null),
                              new ItemRef(Game.Oot, "OOT_BOW", "Bow"), null, playerId, playerId, false),
            new ItemPlacement(new CheckRef(Game.Oot, "CHECK_B", "", [], null, "", null),
                              new ItemRef(Game.Oot, "OOT_BOMB_BAG", "Bomb Bag"), null, playerId, playerId, false),
        ],
    };

    private static string Hello(int player, string name, string teamId = "team") =>
        RelayProtocol.BuildHello(MakeSeed((ushort)player, teamId), "cafe", name, 0);

    private (Wire Source, Wire Teammate, Wire Rival) JoinTwoTeams(RelayRules rules)
    {
        var port = StartEngine(rules);
        var source = new Wire(port);
        var teammate = new Wire(port);
        var rival = new Wire(port);
        source.Send(Hello(1, "source"));
        teammate.Send(Hello(2, "teammate"));
        rival.Send(Hello(2, "rival", "rivals"));
        Assert.NotNull(source.ReceiveType(RelayProtocol.Ready));
        Assert.NotNull(teammate.ReceiveType(RelayProtocol.Ready));
        Assert.NotNull(rival.ReceiveType(RelayProtocol.Ready));
        return (source, teammate, rival);
    }

    [Fact]
    public void V4FactLogSessionFlow()
    {
        var port = StartEngine();
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        var ready = p1.ReceiveType(RelayProtocol.Ready);
        Assert.NotNull(ready);
        Assert.Contains("\"protocolVersion\":4", ready);

        p1.Send(FactLog.EncodeCatchUp(0));
        Assert.NotNull(p1.ReceiveWhere(FactLog.DecodeLive));

        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        var ack = p1.ReceiveWhere(l => FactLog.DecodeAck(l) is not null);
        Assert.NotNull(ack);
        Assert.Equal(1ul, FactLog.DecodeAck(ack)!.Seq);
        var broadcast = p1.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: not 0 });
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}", broadcast);

        using var p2 = new Wire(port);
        p2.Send(Hello(2, "p2"));
        Assert.NotNull(p2.ReceiveType(RelayProtocol.Ready));
        p2.Send(FactLog.EncodeCatchUp(0));
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}",
                     p2.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: not 0 }));
        Assert.NotNull(p2.ReceiveWhere(FactLog.DecodeLive));

        p1.Send(SeedManifest.Build(MakeSeed(1)));
        var forwarded = p2.ReceiveType(RelayProtocol.SeedManifest);
        Assert.NotNull(forwarded);
        Assert.Equal(1, (int)SeedManifest.Parse(forwarded)!.Seed.PlayerId);
        Assert.True(File.Exists(Path.Combine(_store, "room_protocol-test_team_p1.manifest.json")));

        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        var reAck = p1.ReceiveWhere(l => FactLog.DecodeAck(l) is not null);
        Assert.Equal(1ul, FactLog.DecodeAck(reAck!)!.Seq);

        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}");
        Assert.Equal("{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}",
                     p2.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: 2 }));

        Assert.Equal(
            "{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}\n"
            + "{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}\n",
            File.ReadAllText(Path.Combine(_store, "room_protocol-test_team.v4.jsonl")));
    }

    [Fact]
    public void TeamsSharingASeedKeepSeparateProgressButShareTheLobby()
    {
        var port = StartEngine();
        using var red = new Wire(port);
        using var blue = new Wire(port);
        red.Send(Hello(1, "red"));
        blue.Send(Hello(1, "blue", "rivals"));
        Assert.NotNull(red.ReceiveType(RelayProtocol.Ready));
        Assert.NotNull(blue.ReceiveType(RelayProtocol.Ready));
        red.Send(FactLog.EncodeCatchUp(0));
        blue.Send(FactLog.EncodeCatchUp(0));
        Assert.NotNull(red.ReceiveWhere(FactLog.DecodeLive));
        Assert.NotNull(blue.ReceiveWhere(FactLog.DecodeLive));

        red.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        Assert.Equal(1ul, FactLog.DecodeAck(red.ReceiveWhere(l => FactLog.DecodeAck(l) is not null)!)!.Seq);
        blue.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}");
        Assert.Equal(1ul, FactLog.DecodeAck(blue.ReceiveWhere(l => FactLog.DecodeAck(l) is not null)!)!.Seq);
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}",
                     blue.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: not 0 }));
        Assert.Null(blue.ReceiveWhere(l => FactLog.DecodeFact(l) is { CheckId: "CHECK_A" }, timeoutMs: 400));

        red.Send(SeedManifest.Build(MakeSeed(1)));
        Assert.Null(blue.ReceiveType(RelayProtocol.SeedManifest, timeoutMs: 400));
        Assert.True(File.Exists(Path.Combine(_store, "room_protocol-test_team_p1.manifest.json")));

        var flag = "{\"type\":\"OOTMM_WORLD_FLAG\",\"seedId\":\"protocol-test\",\"settingsHash\":\"settings\","
                   + "\"teamId\":\"team\",\"playerId\":1,\"playerCount\":2,\"flagGame\":\"oot\","
                   + "\"kind\":\"chest\",\"sceneId\":1,\"flagIndex\":2}";
        var pose = """{"type":"OOTMM_PLAYER_STATE","seedId":"protocol-test","teamId":"team","sourcePlayer":1,"game":"oot","sceneId":5}""";
        red.Send(flag);
        red.Send(pose);
        Assert.Equal(pose, blue.ReceiveWhere(l =>
            RelayProtocol.ExtractStringField(l, "type") is RelayProtocol.WorldFlag or PresencePackets.PlayerState));

        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}\n",
                     File.ReadAllText(Path.Combine(_store, "room_protocol-test_team.v4.jsonl")));
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}\n",
                     File.ReadAllText(Path.Combine(_store, "room_protocol-test_rivals.v4.jsonl")));
    }

    [Theory]
    [InlineData("", "seed")]
    [InlineData("default", "seed")]
    [InlineData("00000000000000000000000000000000", "seed")]
    [InlineData("0123456789abcdef0123456789abcdef", "seed~0123456789abcdef0123456789abcdef")]
    public void SingleTeamSeedsKeepTheirExistingStore(string teamId, string expected)
    {
        Assert.Equal(expected, V4RoomStore.ProgressRoom("seed", teamId));
    }

    [Fact]
    public void SpoofedFactAndForeignPlayerAreDropped()
    {
        var port = StartEngine();
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        Assert.NotNull(p1.ReceiveType(RelayProtocol.Ready));

        p1.Send("{\"v\":4,\"p\":2,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        var ack = p1.ReceiveWhere(l => FactLog.DecodeAck(l) is not null);
        Assert.Equal(1, (int)FactLog.DecodeAck(ack!)!.SourcePlayer);
        Assert.Equal(1ul, FactLog.DecodeAck(ack!)!.Seq);
    }

    [Fact]
    public void LegacyProtocolHellosRefused()
    {
        var port = StartEngine();
        using var v2 = new Wire(port);
        v2.Send(Hello(1, "old").Replace("\"protocolVersion\":4", "\"protocolVersion\":2"));
        var status = v2.ReceiveType(RelayProtocol.Status);
        Assert.NotNull(status);
        Assert.Contains("protocol v4 required", status);

        using var handshake = new Wire(port);
        handshake.Send("{\"type\":\"OOTMM_HANDSHAKE\",\"seedId\":\"protocol-test\",\"clientName\":\"old\"}");
        Assert.NotNull(handshake.ReceiveType(RelayProtocol.ProtocolError));
    }

    [Fact]
    public void SideChannelForwardingIsBoundToTheSender()
    {
        var port = StartEngine();
        using var p1 = new Wire(port);
        using var p2 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        p2.Send(Hello(2, "p2"));
        Assert.NotNull(p1.ReceiveType(RelayProtocol.Ready));
        Assert.NotNull(p2.ReceiveType(RelayProtocol.Ready));

        var pose = "{\"type\":\"OOTMM_PLAYER_STATE\",\"seedId\":\"protocol-test\",\"teamId\":\"team\","
                   + "\"sourcePlayer\":1,\"game\":\"oot\",\"sceneId\":5}";
        p1.Send(pose.Replace("\"sourcePlayer\":1", "\"sourcePlayer\":2"));
        p1.Send(pose);
        var received = p2.ReceiveType(PresencePackets.PlayerState);
        Assert.Equal(pose, received);
        Assert.Null(p2.ReceiveType(PresencePackets.PlayerState, timeoutMs: 400));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void PvpForwardingRespectsHostRule(bool? enabled)
    {
        var port = StartEngine(enabled is null ? null : new RelayRules { Pvp = enabled.Value, FriendlyFire = true });
        using var p1 = new Wire(port);
        using var p2 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        p2.Send(Hello(2, "p2"));
        Assert.NotNull(p1.ReceiveType(RelayProtocol.Ready));
        Assert.NotNull(p2.ReceiveType(RelayProtocol.Ready));

        var hit = """{"type":"OOTMM_PVP_HIT","seedId":"protocol-test","teamId":"team","sourcePlayer":1,"targetPlayer":2,"seq":1,"damage":8,"pvp":true}""";
        var pose = """{"type":"OOTMM_PLAYER_STATE","seedId":"protocol-test","teamId":"team","sourcePlayer":1,"game":"oot","sceneId":5}""";
        p1.Send(hit);
        p1.Send(pose);
        var first = p2.ReceiveWhere(line =>
            RelayProtocol.ExtractStringField(line, "type") is PresencePackets.PvpHit or PresencePackets.PlayerState);
        if (enabled == true)
        {
            Assert.Equal(hit, first);
            Assert.Equal(pose, p2.ReceiveType(PresencePackets.PlayerState));
        }
        else
        {
            Assert.Equal(pose, first);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PvpHitsReachTeammatesOnlyWithFriendlyFire(bool friendlyFire)
    {
        var (source, teammate, rival) = JoinTwoTeams(new RelayRules { Pvp = true, FriendlyFire = friendlyFire });
        using var a = source;
        using var b = teammate;
        using var c = rival;

        var hit = """{"type":"OOTMM_PVP_HIT","seedId":"protocol-test","teamId":"team","sourcePlayer":1,"targetPlayer":2,"seq":1,"damage":8,"pvp":true}""";
        source.Send(hit);
        Assert.Equal(hit, rival.ReceiveType(PresencePackets.PvpHit));
        var toTeammate = teammate.ReceiveType(PresencePackets.PvpHit, timeoutMs: friendlyFire ? 3000 : 400);
        Assert.Equal(friendlyFire ? hit : null, toTeammate);
    }

    [Theory]
    [InlineData(DeathLinkMode.Off, false, false)]
    [InlineData(DeathLinkMode.Team, true, false)]
    [InlineData(DeathLinkMode.Global, true, true)]
    public void DeathLinkReachesOnlyWhoTheModeIncludes(DeathLinkMode mode, bool toTeammate, bool toRival)
    {
        var (source, teammate, rival) = JoinTwoTeams(new RelayRules { DeathLink = mode });
        using var a = source;
        using var b = teammate;
        using var c = rival;

        var death = """{"type":"OOTMM_DEATH_LINK","seedId":"protocol-test","teamId":"team","sourcePlayer":1,"seq":3}""";
        source.Send(death);
        Assert.Equal(toTeammate ? death : null,
                     teammate.ReceiveType(PresencePackets.DeathLink, timeoutMs: toTeammate ? 3000 : 400));
        Assert.Equal(toRival ? death : null,
                     rival.ReceiveType(PresencePackets.DeathLink, timeoutMs: toRival ? 3000 : 400));
    }

    [Fact]
    public void DeathLinkIsOffByDefaultAndAnnounced()
    {
        Assert.Equal(DeathLinkMode.Off, new RelayRules().DeathLink);
        var port = StartEngine(new RelayRules { DeathLink = DeathLinkMode.Team });
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        Assert.Contains("\"deathLinkMode\":\"team\"", p1.ReceiveType(RelayProtocol.SessionMeta));
    }

    [Theory]
    [InlineData("off", DeathLinkMode.Off)]
    [InlineData("false", DeathLinkMode.Off)]
    [InlineData("team", DeathLinkMode.Team)]
    [InlineData("TEAM", DeathLinkMode.Team)]
    [InlineData("global", DeathLinkMode.Global)]
    [InlineData("true", DeathLinkMode.Global)]
    [InlineData("nonsense", null)]
    public void DeathLinkModeParsesCurrentAndLegacyValues(string value, DeathLinkMode? expected) =>
        Assert.Equal(expected, DeathLinkModes.Parse(value));

    [Fact]
    public void SessionMetaAnnouncesFriendlyFire()
    {
        var port = StartEngine(new RelayRules { Pvp = true, FriendlyFire = true });
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        var meta = p1.ReceiveType(RelayProtocol.SessionMeta);
        Assert.NotNull(meta);
        Assert.Contains("\"friendlyFire\":true", meta);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(250, 250)]
    [InlineData(5, 10)]
    [InlineData(5000, 1000)]
    public void SessionMetaAnnouncesPvpDamage(int configured, int announced)
    {
        var port = StartEngine(new RelayRules { Pvp = true, PvpDamagePercent = configured });
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        var meta = p1.ReceiveType(RelayProtocol.SessionMeta);
        Assert.NotNull(meta);
        Assert.Contains($"\"pvpDamage\":{announced},", meta);
    }

    [Fact]
    public void Allowlist_RejectsProgressAndUnboundFrames()
    {
        var client = new VerifiedClientIdentity("protocol-test", "settings", "team", 1, 2);
        var flag = "{\"type\":\"OOTMM_WORLD_FLAG\",\"seedId\":\"protocol-test\",\"settingsHash\":\"settings\","
                   + "\"teamId\":\"team\",\"playerId\":1,\"playerCount\":2,\"flagGame\":\"oot\","
                   + "\"kind\":\"chest\",\"sceneId\":1,\"flagIndex\":2}";
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(flag, client));
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(
            flag.Replace("\"playerId\":1", "\"playerId\":2"), client));
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(
            flag.Replace("\"kind\":\"chest\"", "\"kind\":\"other\""), client));
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(
            "{\"type\":\"OOTMM_CHECK_CLAIM\",\"seedId\":\"protocol-test\",\"teamId\":\"team\"}", client));
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(
            "{\"type\":\"OOTMM_UNKNOWN\",\"seedId\":\"protocol-test\",\"teamId\":\"team\"}", client));

        var death = "{\"type\":\"OOTMM_DEATH_LINK\",\"seedId\":\"protocol-test\",\"teamId\":\"team\","
                    + "\"sourcePlayer\":1,\"seq\":7}";
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(death, client));
        Assert.False(SideChannelPolicy.IsRoomPacketAllowed(death.Replace("\"seq\":7", "\"seq\":0"), client));
    }

    [Fact]
    public void MaintenanceLeaseExcludesConcurrentUse()
    {
        Directory.CreateDirectory(_store);
        using (new FileStream(Path.Combine(_store, ".ootmm-world-state.lock"),
                              FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var report = new RelayEngine(_store, new RelayRules()) { Log = _ => { } }
                .ProvisionRoomManifests(["missing.json"]);
            Assert.Contains("in use", report.Message);
        }
    }

    [Fact]
    public void ResetMarkerFailsJoinsClosed()
    {
        Directory.CreateDirectory(_store);
        File.WriteAllText(V4RoomStore.ResetMarkerPathFor(
            _store, V4RoomStore.ProgressRoom("protocol-test", "team")), "{}");
        var port = StartEngine();
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        var status = p1.ReceiveType(RelayProtocol.Status);
        Assert.NotNull(status);
        Assert.Contains("epoch reset in progress", status);
    }
}

public class V4RoomStoreTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ootmm-v4store-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_RepairsTornTailOnly()
    {
        var dir = TempDir();
        File.WriteAllText(V4RoomStore.PathFor(dir, "r"),
            "{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n{\"v\":4,\"seq\":2,\"p\":1");
        var store = new V4RoomStore();
        Assert.True(store.Load(dir, "r"));
        Assert.Single(store.Lines);
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n",
                     File.ReadAllText(V4RoomStore.PathFor(dir, "r")));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_FailsClosedOnCorruptCommittedLines()
    {
        var dir = TempDir();
        var store = new V4RoomStore();

        File.WriteAllText(V4RoomStore.PathFor(dir, "r"), "not json\n");
        Assert.False(store.Load(dir, "r"));

        File.WriteAllText(V4RoomStore.PathFor(dir, "r"),
            "{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n");
        Assert.False(new V4RoomStore().Load(dir, "r"));

        File.WriteAllText(V4RoomStore.PathFor(dir, "r"),
            "{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n"
            + "{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n");
        Assert.False(new V4RoomStore().Load(dir, "r"));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void SafeCheckIds_RejectEscapesAndNonAscii()
    {
        Assert.True(V4RoomStore.IsSafeCheckId("OOT_CHECK_A-1"));
        Assert.False(V4RoomStore.IsSafeCheckId(""));
        Assert.False(V4RoomStore.IsSafeCheckId("bad\"quote"));
        Assert.False(V4RoomStore.IsSafeCheckId("back\\slash"));
        Assert.False(V4RoomStore.IsSafeCheckId("ctrl" + (char)1));
        Assert.False(V4RoomStore.IsSafeCheckId(new string('x', 257)));
    }
}
