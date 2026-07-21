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

    private static Seed MakeSeed(ushort playerId) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "protocol-test",
        SettingsHash = "settings",
        TeamId = "team",
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

    private static string Hello(int player, string name) =>
        RelayProtocol.BuildHello(MakeSeed((ushort)player), "cafe", name, 0);

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

        // Publish (no seq) -> durable ack with the assigned seq + broadcast (sender included).
        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        var ack = p1.ReceiveWhere(l => FactLog.DecodeAck(l) is not null);
        Assert.NotNull(ack);
        Assert.Equal(1ul, FactLog.DecodeAck(ack)!.Seq);
        var broadcast = p1.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: not 0 });
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}", broadcast);

        // Second client: catch-up streams the stored fact, then goes live.
        using var p2 = new Wire(port);
        p2.Send(Hello(2, "p2"));
        Assert.NotNull(p2.ReceiveType(RelayProtocol.Ready));
        p2.Send(FactLog.EncodeCatchUp(0));
        Assert.Equal("{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}",
                     p2.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: not 0 }));
        Assert.NotNull(p2.ReceiveWhere(FactLog.DecodeLive));

        // Manifest upload registers durably and forwards to connected clients.
        p1.Send(SeedManifest.Build(MakeSeed(1)));
        var forwarded = p2.ReceiveType(RelayProtocol.SeedManifest);
        Assert.NotNull(forwarded);
        Assert.Equal(1, (int)SeedManifest.Parse(forwarded)!.Seed.PlayerId);
        Assert.True(File.Exists(Path.Combine(_store, "room_protocol-test_p1.manifest.json")));

        // Duplicate publish re-acks idempotently with the existing seq.
        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}");
        var reAck = p1.ReceiveWhere(l => FactLog.DecodeAck(l) is not null);
        Assert.Equal(1ul, FactLog.DecodeAck(reAck!)!.Seq);

        // Live publish broadcasts to every v4 room client.
        p1.Send("{\"v\":4,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}");
        Assert.Equal("{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}",
                     p2.ReceiveWhere(l => FactLog.DecodeFact(l) is { Seq: 2 }));

        Assert.Equal(
            "{\"v\":4,\"seq\":1,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_A\"}\n"
            + "{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"CHECK_B\"}\n",
            File.ReadAllText(Path.Combine(_store, "room_protocol-test.v4.jsonl")));
    }

    [Fact]
    public void SpoofedFactAndForeignPlayerAreDropped()
    {
        var port = StartEngine();
        using var p1 = new Wire(port);
        p1.Send(Hello(1, "p1"));
        Assert.NotNull(p1.ReceiveType(RelayProtocol.Ready));

        // A fact for another player is dropped: no ack ever arrives for it.
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
        p1.Send(pose.Replace("\"sourcePlayer\":1", "\"sourcePlayer\":2")); // spoofed: dropped
        p1.Send(pose);
        var received = p2.ReceiveType(PresencePackets.PlayerState);
        Assert.Equal(pose, received);
        Assert.Null(p2.ReceiveType(PresencePackets.PlayerState, timeoutMs: 400));
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
        File.WriteAllText(V4RoomStore.ResetMarkerPathFor(_store, "protocol-test"), "{}");
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

        // Committed garbage is never repaired away.
        File.WriteAllText(V4RoomStore.PathFor(dir, "r"), "not json\n");
        Assert.False(store.Load(dir, "r"));

        // Non-contiguous seq.
        File.WriteAllText(V4RoomStore.PathFor(dir, "r"),
            "{\"v\":4,\"seq\":2,\"p\":1,\"g\":\"oot\",\"c\":\"A\"}\n");
        Assert.False(new V4RoomStore().Load(dir, "r"));

        // Duplicate natural key.
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
