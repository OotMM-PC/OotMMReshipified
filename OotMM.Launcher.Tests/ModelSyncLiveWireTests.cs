using System.IO.Compression;
using OotMM.Core;
using OotMM.Core.Relay;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

/// <summary>Runs the whole live stack — relay engine, both launchers' MultiworldSync clients
/// over real sockets, both coordinators — the seam the direct coordinator test cannot cover.
/// </summary>
public class ModelSyncLiveWireTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ootmm-modelsync-").FullName;
    private readonly CancellationTokenSource _stop = new();
    private RelayEngine? _engine;
    private Thread? _thread;
    private MultiworldSync? _ms1;
    private MultiworldSync? _ms2;
    private MultiworldSync? _ms3;

    public void Dispose()
    {
        _ms1?.Dispose();
        _ms2?.Dispose();
        _ms3?.Dispose();
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(5));
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Seed MakeSeed(ushort playerId, ushort playerCount) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "modelsync-live",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = playerId,
        PlayerCount = playerCount,
        SessionEpoch = 4,
        Placements =
        [
            new ItemPlacement(new CheckRef(Game.Oot, "CHECK_A", "", [], null, "", null),
                              new ItemRef(Game.Oot, "OOT_BOW", "Bow"), null, playerId, playerId, false),
        ],
    };

    private int StartEngine()
    {
        _engine = new RelayEngine(Path.Combine(_root, "store"), new RelayRules()) { Log = _ => { } };
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

    private Core.Session MakeSession(ushort playerId, ushort playerCount = 2)
    {
        var side = Directory.CreateDirectory(Path.Combine(_root, $"side{playerId}")).FullName;
        var seedPath = Path.Combine(side, "seed.json");
        File.WriteAllText(seedPath, "{}");
        var session = new Core.Session
        {
            SeedPath = seedPath,
            OotExe = Path.Combine(side, "oot", "soh.exe"),
            MmExe = Path.Combine(side, "mm", "2ship.exe"),
            Seed = MakeSeed(playerId, playerCount),
            V4Planned = true,
        };
        Assert.Null(SessionPaths.PrepareSession(session));
        return session;
    }

    [Fact]
    public void ModelSetTransfersAcrossTheRealRelayAndInstalls()
    {
        var port = StartEngine();

        var session1 = MakeSession(1);
        var session2 = MakeSession(2);
        var mm1Mods = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(session1.MmExe)!, "mods")).FullName;
        var mm2Mods = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(session2.MmExe)!, "mods")).FullName;

        var model = Path.Combine(mm1Mods, "model.o2r");
        using (var file = File.Create(model))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using var s = zip.CreateEntry("objects/object_link_child/gLinkHumanSkel").Open();
            // Spans two chunks, and the byte spread makes the wire base64 carry '+' — the
            // character a general JSON escaper mangles past the relay's raw field scanner.
            var payload = new byte[80 * 1024];
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)i;
            }

            Assert.Contains('+', Convert.ToBase64String(payload));
            s.Write(payload);
        }

        _ms1 = new MultiworldSync(session1, "127.0.0.1", (ushort)port, "P1");
        _ms2 = new MultiworldSync(session2, "127.0.0.1", (ushort)port, "P2");
        var mc1 = MakeCoordinator(session1, [new ModelShareSet(Game.Mm, [model])], _ms1);
        var mc2 = MakeCoordinator(session2, [], _ms2);
        var log = new List<string>();
        mc1.Logged += log.Add;
        mc2.Logged += log.Add;

        var installed = Path.Combine(mm2Mods, "zz_coop_p01.o2r");
        var sidecar = installed + ".sources";
        var none = new HashSet<string>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline
               && !(File.Exists(sidecar)
                    && File.ReadAllText(sidecar).StartsWith("set|", StringComparison.Ordinal)))
        {
            _ms1.Tick(none, session1.OotInbox);
            mc1.Tick(_ms1.Ready, _ms1.ConnectionGeneration);
            mc1.PumpSend(_ms1.Ready);
            _ms2.Tick(none, session2.OotInbox);
            mc2.Tick(_ms2.Ready, _ms2.ConnectionGeneration);
            mc2.PumpSend(_ms2.Ready);
            Thread.Sleep(20);
        }

        Assert.True(File.Exists(installed), "transfer never installed; log: " + string.Join(" | ", log));
        Assert.StartsWith("set|", File.ReadAllText(sidecar));
        using var ported = ZipFile.OpenRead(installed);
        Assert.Equal("p01objs/object_link_child/gLinkHumanSkel", ported.Entries.Single().FullName);
    }

    [Fact]
    public void ThirdPlayerJoiningMidTransferDoesNotStallTheOthers()
    {
        var port = StartEngine();
        var session1 = MakeSession(1, 3);
        var session2 = MakeSession(2, 3);
        var session3 = MakeSession(3, 3);
        var mm1Mods = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(session1.MmExe)!, "mods")).FullName;
        var sidecar2 = Path.Combine(Path.GetDirectoryName(session2.MmExe)!, "mods", "zz_coop_p01.o2r.sources");
        var sidecar3 = Path.Combine(Path.GetDirectoryName(session3.MmExe)!, "mods", "zz_coop_p01.o2r.sources");

        var model = Path.Combine(mm1Mods, "model.o2r");
        using (var file = File.Create(model))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using var s = zip.CreateEntry("objects/object_link_child/gLinkHumanSkel", CompressionLevel.NoCompression).Open();
            var payload = new byte[3 * 1024 * 1024];
            new Random(7).NextBytes(payload);
            s.Write(payload);
        }

        _ms1 = new MultiworldSync(session1, "127.0.0.1", (ushort)port, "P1");
        _ms2 = new MultiworldSync(session2, "127.0.0.1", (ushort)port, "P2");
        var mc1 = MakeCoordinator(session1, [new ModelShareSet(Game.Mm, [model])], _ms1);
        var mc2 = MakeCoordinator(session2, [], _ms2);
        ModelSyncCoordinator? mc3 = null;
        var log = new List<string>();
        mc1.Logged += log.Add;
        mc2.Logged += log.Add;

        var none = new HashSet<string>();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        var finished = () => File.Exists(sidecar2) && File.Exists(sidecar3);
        for (var loop = 0; DateTime.UtcNow < deadline && !finished(); loop++)
        {
            _ms1.Tick(none, session1.OotInbox);
            mc1.Tick(_ms1.Ready, _ms1.ConnectionGeneration);
            if (loop % 5 == 0)
            {
                mc1.PumpSend(_ms1.Ready);
            }

            _ms2.Tick(none, session2.OotInbox);
            mc2.Tick(_ms2.Ready, _ms2.ConnectionGeneration);
            if (mc3 is null && log.Any(line => line.StartsWith("Sending", StringComparison.Ordinal)))
            {
                _ms3 = new MultiworldSync(session3, "127.0.0.1", (ushort)port, "P3");
                mc3 = MakeCoordinator(session3, [], _ms3);
                mc3.Logged += log.Add;
            }

            if (_ms3 is not null && mc3 is not null)
            {
                _ms3.Tick(none, session3.OotInbox);
                mc3.Tick(_ms3.Ready, _ms3.ConnectionGeneration);
            }

            Thread.Sleep(20);
        }

        Assert.True(finished(), "both receivers never installed; log: " + string.Join(" | ", log));
        Assert.StartsWith("set|", File.ReadAllText(sidecar2));
        Assert.StartsWith("set|", File.ReadAllText(sidecar3));
    }

    private static ModelSyncCoordinator MakeCoordinator(
        Core.Session session, IReadOnlyList<ModelShareSet> shares, MultiworldSync sync)
    {
        var ootMods = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(session.OotExe)!, "mods")).FullName;
        var mmMods = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(session.MmExe)!, "mods")).FullName;
        var coordinator = new ModelSyncCoordinator(
            session.Seed, Path.Combine(session.SessionDir, "models"), 100, shares,
            new Dictionary<Game, string> { [Game.Oot] = ootMods, [Game.Mm] = mmMods },
            new Dictionary<Game, IReadOnlyList<string>> { [Game.Oot] = [], [Game.Mm] = [] },
            sync.SendModelLine);
        sync.ModelLineReceived += coordinator.HandleLine;
        return coordinator;
    }
}
