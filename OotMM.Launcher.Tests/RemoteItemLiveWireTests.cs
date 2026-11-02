using OotMM.Core;
using OotMM.Core.Relay;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public class RemoteItemLiveWireTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ootmm-remote-item-").FullName;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<MultiworldSync> _syncs = [];
    private readonly Dictionary<MultiworldSync, HashSet<string>> _completed = [];
    private Thread? _thread;

    public void Dispose()
    {
        foreach (var sync in _syncs)
        {
            sync.Dispose();
        }

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

    private int StartEngine()
    {
        var engine = new RelayEngine(Path.Combine(_root, "store"), new RelayRules()) { Log = _ => { } };
        _thread = new Thread(() => engine.Run(0, _stop.Token)) { IsBackground = true };
        _thread.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (engine.BoundPort == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.NotEqual(0, engine.BoundPort);
        return engine.BoundPort;
    }

    private MultiworldSync Join(int port, ushort playerId, string itemId)
    {
        var side = Directory.CreateDirectory(Path.Combine(_root, $"p{playerId}")).FullName;
        var seedPath = Path.Combine(side, "seed.json");
        File.WriteAllText(seedPath, "{}");
        var session = new Core.Session
        {
            SeedPath = seedPath,
            OotExe = Path.Combine(side, "oot", "soh.exe"),
            MmExe = Path.Combine(side, "mm", "2ship.exe"),
            Seed = new Seed
            {
                Format = Seed.CurrentFormat,
                GeneratorVersion = Seed.RequiredGeneratorVersion,
                SeedId = "remote-item",
                SettingsHash = "settings",
                TeamId = "team",
                PlayerId = playerId,
                PlayerCount = 2,
                SessionEpoch = 4,
                Placements =
                [
                    new ItemPlacement(new CheckRef(Game.Oot, "CHECK_A", "", [], null, "", null),
                                      new ItemRef(Game.Oot, itemId, itemId), null,
                                      (ushort)(3 - playerId), playerId, false),
                ],
            },
            V4Planned = true,
        };
        Assert.Null(SessionPaths.PrepareSession(session));
        var sync = new MultiworldSync(session, "127.0.0.1", (ushort)port, $"team{playerId}");
        _syncs.Add(sync);
        _completed[sync] = [];
        return sync;
    }

    private void PumpUntil(Func<bool> done, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !done())
        {
            foreach (var sync in _syncs)
            {
                sync.Tick(_completed[sync], "");
            }

            Thread.Sleep(20);
        }
    }

    [Fact]
    public void LiveItemFromAnotherPlayerRaisesOneReceiptNamingTheSender()
    {
        var port = StartEngine();
        var sender = Join(port, 1, "OOT_HAMMER");
        var receiver = Join(port, 2, "OOT_BOW");
        var received = new List<(ushort Source, string Item)>();
        receiver.RemoteItemReceived += (source, placement) => received.Add((source, placement.Item.Id));
        PumpUntil(() => sender.Ready && receiver.Ready && receiver.Live);
        Assert.True(receiver.Live);
        PumpUntil(() => false, seconds: 1);

        _completed[sender].Add("oot:CHECK_A");
        PumpUntil(() => received.Count > 0);
        PumpUntil(() => false, seconds: 1);

        Assert.Equal((1, "OOT_HAMMER"), Assert.Single(received));
        Assert.Equal(1, receiver.RemoteItemCounts["OOT_HAMMER"]);
    }

    [Fact]
    public void ItemsCaughtUpOnJoinCountWithoutRaisingReceipts()
    {
        var port = StartEngine();
        var sender = Join(port, 1, "OOT_HAMMER");
        PumpUntil(() => sender.Ready);
        _completed[sender].Add("oot:CHECK_A");
        PumpUntil(() => sender.PendingChecks == 0 && sender.Live);

        var receiver = Join(port, 2, "OOT_BOW");
        var received = 0;
        receiver.RemoteItemReceived += (_, _) => received++;
        PumpUntil(() => receiver.RemoteItemCounts.ContainsKey("OOT_HAMMER"));
        PumpUntil(() => false, seconds: 1);

        Assert.Equal(1, receiver.RemoteItemCounts["OOT_HAMMER"]);
        Assert.Equal(0, received);
    }
}
