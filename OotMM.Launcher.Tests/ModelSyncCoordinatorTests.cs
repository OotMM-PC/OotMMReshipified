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

    private static byte[] Noise(int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static void Deliver(List<string> outbox, params ModelSyncCoordinator[] targets)
    {
        var lines = outbox.ToList();
        outbox.Clear();
        foreach (var line in lines)
        {
            foreach (var target in targets)
            {
                target.HandleLine(line);
            }
        }
    }

    [Fact]
    public void ReceiverJoiningMidStreamDoesNotCostAnEarlierReceiverItsProgress()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model, ("objects/object_link_child/gLinkHumanSkel", Noise(5 * 48 * 1024, 1)));
            var aOut = new List<string>();
            var bOut = new List<string>();
            var cOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var c = MakeCoordinator(MakeSeed(3), Path.Combine(dir, "c"), [], [], cOut);
            var bInstalled = Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r");
            var cInstalled = Path.Combine(dir, "c", "mm", "mods", "zz_coop_p01.o2r");

            a.Tick(true, 1);
            Deliver(aOut, b);
            b.Tick(true, 1);
            Deliver(bOut, a);
            a.PumpSend(true);
            Deliver(aOut, b);
            Assert.False(File.Exists(bInstalled));

            a.Tick(true, 2);
            Deliver(aOut, c);
            c.Tick(true, 1);
            Deliver(cOut, a);
            for (var pump = 0; pump < 3; pump++)
            {
                a.PumpSend(true);
                Deliver(aOut, b, c);
            }

            Assert.StartsWith("set|", File.ReadAllText(bInstalled + ".sources"));
            Assert.StartsWith("set|", File.ReadAllText(cInstalled + ".sources"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AChangedSetNeverInstallsMixedWithThePreviousOne()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var first = Path.Combine(dir, "first.o2r");
            var second = Path.Combine(dir, "second.o2r");
            MakeArchive(first, ("objects/object_link_child/gLinkHumanSkel", [1]));
            MakeArchive(second, ("objects/object_link_child/gLinkHumanHeadDL", [2]));
            var aOut = new List<string>();
            var swappedOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [first, second])], [], aOut);
            var swapped = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a2"),
                [new ModelShareSet(Game.Mm, [second, first])], [], swappedOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var sidecar = Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r.sources");

            for (var round = 0; round < 6 && !File.Exists(sidecar); round++)
            {
                a.Tick(true, 1);
                Deliver(aOut, b);
                b.Tick(true, 1);
                Deliver(bOut, a);
                a.PumpSend(true);
                Deliver(aOut, b);
            }

            var original = File.ReadAllText(sidecar);
            Assert.StartsWith("set|", original);

            swapped.Tick(true, 1);
            var announces = swappedOut.Where(line => line.Contains("\"game\":\"mm\"")).ToList();
            Assert.Equal(2, announces.Count);

            b.HandleLine(announces[0]);
            b.Tick(true, 1);
            Assert.Equal(original, File.ReadAllText(sidecar));

            b.HandleLine(announces[1]);
            b.Tick(true, 1);
            var parts = File.ReadAllText(sidecar).Split('|');
            var before = original.Split('|');
            Assert.Equal((before[2], before[1]), (parts[1], parts[2]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetFinishingDuringAGameLaunchMountsLiveInsteadOfMissingTheBoot()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model, ("objects/object_link_child/gLinkHumanSkel", Noise(5 * 48 * 1024, 2)));
            var aOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var mounted = new List<string>();
            b.LiveArchiveReady += (_, path, _) => mounted.Add(path);

            a.Tick(true, 1);
            Deliver(aOut, b);
            b.Tick(true, 1);
            Deliver(bOut, a);
            a.PumpSend(true);
            Deliver(aOut, b);
            Assert.Empty(mounted);

            b.NoteGameLaunching(Game.Mm);
            a.PumpSend(true);
            Deliver(aOut, b);

            Assert.Single(mounted);
            Assert.False(File.Exists(Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r.sources")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetInstalledBeforeLaunchIsNotMountedAgain()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var model = Path.Combine(dir, "model.o2r");
            MakeArchive(model, ("objects/object_link_child/gLinkHumanSkel", [1]));
            var aOut = new List<string>();
            var bOut = new List<string>();
            var a = MakeCoordinator(MakeSeed(1), Path.Combine(dir, "a"),
                [new ModelShareSet(Game.Mm, [model])], [], aOut);
            var b = MakeCoordinator(MakeSeed(2), Path.Combine(dir, "b"), [], [], bOut);
            var sidecar = Path.Combine(dir, "b", "mm", "mods", "zz_coop_p01.o2r.sources");
            for (var round = 0; round < 6 && !File.Exists(sidecar); round++)
            {
                a.Tick(true, 1);
                Deliver(aOut, b);
                b.Tick(true, 1);
                Deliver(bOut, a);
                a.PumpSend(true);
                Deliver(aOut, b);
            }

            Assert.True(File.Exists(sidecar));
            var mounted = new List<string>();
            b.LiveArchiveReady += (_, path, _) => mounted.Add(path);
            b.NoteGameLaunching(Game.Mm);
            b.Tick(true, 1, Game.Mm);
            Assert.Empty(mounted);
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
