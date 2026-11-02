using System.IO.Compression;
using OotMM.Core;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public class ModelSyncCoordinatorTests
{
    private static Seed MakeSeed(ushort playerId) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "seed",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = playerId,
        PlayerCount = 2,
    };

    private static void MakeArchive(string path, params (string Path, byte[] Data)[] entries)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (entryPath, data) in entries)
        {
            using var s = zip.CreateEntry(entryPath).Open();
            s.Write(data);
        }
    }

    private static ModelSyncCoordinator MakeCoordinator(
        Seed seed, string root, IReadOnlyList<ModelShareSet> shares,
        IReadOnlyList<string> mmFillArchives, List<string> outbox)
    {
        var ootMods = Directory.CreateDirectory(Path.Combine(root, "oot", "mods")).FullName;
        var mmMods = Directory.CreateDirectory(Path.Combine(root, "mm", "mods")).FullName;
        return new ModelSyncCoordinator(
            seed, Path.Combine(root, "cache"), 100, shares,
            new Dictionary<Game, string> { [Game.Oot] = ootMods, [Game.Mm] = mmMods },
            new Dictionary<Game, IReadOnlyList<string>> { [Game.Oot] = [], [Game.Mm] = mmFillArchives },
            line =>
            {
                outbox.Add(line);
                return true;
            });
    }

    [Fact]
    public void TransferInstallsTheRemoteSetOverTheVanillaFill()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model,
                ("objects/object_link_child/gLinkHumanSkel", [1, 2, 3]),
                ("objects/ot_obj_link_child/gLinkChildSkel", [4, 5]));
            var baseArchive = Path.Combine(dir, "mm.o2r");
            MakeArchive(baseArchive,
                ("objects/object_link_child/gLinkHumanSkel", [9]),
                ("objects/ot_obj_link_boy/gLinkAdultSkel", [8]),
                ("objects/gameplay_keep/anything", [9]));

            var aOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [baseArchive], bOut);
            var expectedEntries = new[]
            {
                // The synced set composes over the vanilla base: the set's entries win their
                // paths, and base-only assets (the adult skeleton here) stay resolvable.
                "p01objs/object_link_child/gLinkHumanSkel",
                "p01objs/ot_obj_link_boy/gLinkAdultSkel",
                "p01objs/ot_obj_link_child/gLinkChildSkel",
            };

            var installed = Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r");
            var sidecar = installed + ".sources";

            // The fill lands before any traffic, so an offline peer still resolves to vanilla.
            b.Tick(ready: false, generation: 0);
            Assert.True(File.Exists(installed));
            Assert.StartsWith("fill|", File.ReadAllText(sidecar));
            using (var fill = ZipFile.OpenRead(installed))
            {
                Assert.Equal(
                    ["p01objs/object_link_child/gLinkHumanSkel", "p01objs/ot_obj_link_boy/gLinkAdultSkel"],
                    fill.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());
            }

            for (var i = 0; i < 10 && !File.ReadAllText(sidecar).StartsWith("set|"); i++)
            {
                a.Tick(ready: true, generation: 1);
                Drain(aOut, b);
                b.Tick(ready: true, generation: 1);
                Drain(bOut, a);
                a.PumpSend(ready: true);
                Drain(aOut, b);
            }

            Assert.StartsWith("set|", File.ReadAllText(sidecar));
            using (var zip = ZipFile.OpenRead(installed))
            {
                Assert.Equal(expectedEntries, zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());
                using var human = zip.GetEntry("p01objs/object_link_child/gLinkHumanSkel")!.Open();
                Assert.Equal(1, human.ReadByte()); // the set's bytes, not the base's
            }

            // Player 1 relaunches with no models: the empty-set announce must retire the
            // installed set back to pure vanilla fill.
            var emptyOut = new List<string>();
            var aEmpty = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a2"), [
                new ModelShareSet(Game.Mm, []),
            ], [], emptyOut);
            aEmpty.Tick(ready: true, generation: 1);
            Drain(emptyOut, b);
            b.Tick(ready: true, generation: 1);

            Assert.StartsWith("set||base|", File.ReadAllText(sidecar));
            using (var zip = ZipFile.OpenRead(installed))
            {
                Assert.Equal(
                    ["p01objs/object_link_child/gLinkHumanSkel", "p01objs/ot_obj_link_boy/gLinkAdultSkel"],
                    zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList());
                using var human = zip.GetEntry("p01objs/object_link_child/gLinkHumanSkel")!.Open();
                Assert.Equal(9, human.ReadByte()); // back to the base's vanilla bytes
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetArrivingForTheRunningGameBuildsALiveArchiveAndCleansItUp()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model, ("objects/object_link_child/gLinkHumanSkel", [1, 2, 3]));
            var aOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var mods = Path.Combine(dir, "b", "mm", "mods");
            var mounted = new List<(Game Game, string Path, int Player)>();
            b.LiveArchiveReady += (game, path, player) => mounted.Add((game, path, player));

            for (var round = 0; round < 6 && mounted.Count == 0; round++)
            {
                a.Tick(true, 1);
                b.Tick(true, 1, Game.Mm);
                a.PumpSend(true);
                foreach (var line in aOut.ToList())
                {
                    b.HandleLine(line);
                }

                foreach (var line in bOut.ToList())
                {
                    a.HandleLine(line);
                }

                aOut.Clear();
                bOut.Clear();
            }

            var live = Assert.Single(mounted);
            Assert.Equal(Game.Mm, live.Game);
            Assert.Equal(1, live.Player);
            Assert.True(File.Exists(live.Path));
            Assert.False(File.Exists(Path.Combine(mods, "zz_coop_p01.o2r.sources")));

            b.Tick(true, 1, Game.Mm);
            Assert.Single(mounted);

            b.InstallNow();
            Assert.False(File.Exists(live.Path));
            Assert.True(File.Exists(Path.Combine(mods, "zz_coop_p01.o2r")));
            Assert.StartsWith("set|", File.ReadAllText(Path.Combine(mods, "zz_coop_p01.o2r.sources")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PersistedSetsReinstallAtConstructionBeforeAnyTraffic()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model, ("objects/object_link_child/gLinkHumanSkel", [1, 2, 3]));

            var aOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);

            var installed = Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r");
            for (var i = 0; i < 10 && !File.Exists(installed); i++)
            {
                a.Tick(ready: true, generation: 1);
                Drain(aOut, b);
                b.Tick(ready: true, generation: 1);
                Drain(bOut, a);
                a.PumpSend(ready: true);
                Drain(aOut, b);
            }

            Assert.True(File.Exists(installed));
            // A new session with no relay traffic must restore the install from the
            // persisted set table and the blob cache alone.
            File.Delete(installed);
            File.Delete(installed + ".sources");
            var rejoined = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], []);

            Assert.True(File.Exists(installed));
            using var zip = ZipFile.OpenRead(installed);
            Assert.Equal("p01objs/object_link_child/gLinkHumanSkel", zip.Entries.Single().FullName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OverCapSetIsRefusedAndNeverRequested()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var bOut = new List<string>();
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var refusals = new List<string>();
            b.Logged += refusals.Add;

            var seedA = MakeSeed(1);
            var huge = new Core.Models.ModelAnnounce(
                1, Game.Mm, "huge.o2r", new string('a', 64), 200L * 1024 * 1024);
            b.HandleLine(Core.Models.ModelSyncProtocol.BuildAnnounce(seedA, huge));
            b.Tick(ready: true, generation: 1);

            Assert.DoesNotContain(bOut, line => line.Contains("OOTMM_MOD_REQUEST"));
            Assert.Contains(refusals, message => message.Contains("refused"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Drain(List<string> outbox, ModelSyncCoordinator target)
    {
        var lines = outbox.ToList();
        outbox.Clear();
        foreach (var line in lines)
        {
            target.HandleLine(line);
        }
    }
}
