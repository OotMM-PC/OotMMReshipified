using OotMM.Core;
using OotMM.Core.Relay;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public class DeathLinkLiveWireTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ootmm-deathlink-").FullName;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<MultiworldSync> _syncs = [];
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

    private int StartEngine(RelayRules rules)
    {
        var engine = new RelayEngine(Path.Combine(_root, "store"), rules) { Log = _ => { } };
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

    private MultiworldSync Join(int port, ushort playerId, string teamId)
    {
        var side = Directory.CreateDirectory(Path.Combine(_root, $"{teamId}{playerId}")).FullName;
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
                SeedId = "deathlink-live",
                SettingsHash = "settings",
                TeamId = teamId,
                PlayerId = playerId,
                PlayerCount = 2,
                SessionEpoch = 4,
                Placements =
                [
                    new ItemPlacement(new CheckRef(Game.Oot, "CHECK_A", "", [], null, "", null),
                                      new ItemRef(Game.Oot, "OOT_BOW", "Bow"), null, playerId, playerId, false),
                ],
            },
            V4Planned = true,
        };
        Assert.Null(SessionPaths.PrepareSession(session));
        var sync = new MultiworldSync(session, "127.0.0.1", (ushort)port, $"{teamId}{playerId}");
        _syncs.Add(sync);
        return sync;
    }

    private void PumpUntil(Func<bool> done, int seconds = 10)
    {
        var none = new HashSet<string>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !done())
        {
            foreach (var sync in _syncs)
            {
                sync.Tick(none, "");
            }

            Thread.Sleep(20);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeathReachesTeammateOnceAndOtherTeamOnlyWhenGlobal(bool global)
    {
        var port = StartEngine(new RelayRules { DeathLink = global });
        var source = Join(port, 1, "team");
        var teammate = Join(port, 2, "team");
        var rival = Join(port, 2, "rivals");
        PumpUntil(() => _syncs.All(sync => sync.Ready));
        Assert.All(_syncs, sync => Assert.True(sync.Ready));

        source.SubmitDeathLink();
        var teammateDeaths = new List<string>();
        var rivalDeaths = new List<string>();
        PumpUntil(() =>
        {
            teammateDeaths.AddRange(teammate.TakeRemoteDeaths());
            rivalDeaths.AddRange(rival.TakeRemoteDeaths());
            return teammateDeaths.Count > 0 && (!global || rivalDeaths.Count > 0);
        });
        PumpUntil(() =>
        {
            teammateDeaths.AddRange(teammate.TakeRemoteDeaths());
            rivalDeaths.AddRange(rival.TakeRemoteDeaths());
            return false;
        }, seconds: 1);

        var death = Assert.Single(teammateDeaths);
        Assert.Equal(PresencePackets.DeathLink, RelayProtocol.ExtractStringField(death, "type"));
        Assert.Equal(1, RelayProtocol.ExtractIntField(death, "sourcePlayer"));
        Assert.Equal(global ? 1 : 0, rivalDeaths.Count);
        Assert.Empty(source.TakeRemoteDeaths());
        Assert.True(GameIpcProtocol.TryParseDeathLink(GameIpcProtocol.BuildDeathLink(death), out var body));
        Assert.Equal(death, body);
    }
}
