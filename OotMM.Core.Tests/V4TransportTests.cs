using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class V4TransportTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ootmm-v4-tests", Guid.NewGuid().ToString("N"));

    public V4TransportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SelectFacts_FiltersPacesAndBounds()
    {
        var journal = new List<V4JournalCheck>
        {
            new(Game.Oot, "A", Collected: true),
            new(Game.Oot, "B", Collected: false),
            new(Game.Mm, "C", Collected: true),
            new(Game.Oot, "D", Collected: true),
        };
        var acked = new HashSet<string> { V4Transport.NaturalKey(2, Game.Mm, "C") };
        var attempts = new Dictionary<string, TimeSpan>();
        var retry = TimeSpan.FromSeconds(5);

        var first = V4Transport.SelectFactsToPublish(
            journal, 2, acked, attempts, TimeSpan.Zero, retry, maxBatch: 10);
        Assert.Equal(["A", "D"], first.Select(f => f.CheckId));
        Assert.All(first, f => Assert.Equal(0ul, f.Seq));

        Assert.Empty(V4Transport.SelectFactsToPublish(
            journal, 2, acked, attempts, TimeSpan.FromSeconds(2), retry, 10));
        Assert.Equal(2, V4Transport.SelectFactsToPublish(
            journal, 2, acked, attempts, TimeSpan.FromSeconds(6), retry, 10).Count);

        attempts.Clear();
        Assert.Single(V4Transport.SelectFactsToPublish(
            journal, 2, acked, attempts, TimeSpan.FromSeconds(20), retry, maxBatch: 1));
    }

    [Theory]
    [InlineData(3ul, 5ul, V4FerryAction.Duplicate)]
    [InlineData(5ul, 5ul, V4FerryAction.Duplicate)]
    [InlineData(6ul, 5ul, V4FerryAction.Append)]
    [InlineData(7ul, 5ul, V4FerryAction.Gap)]
    [InlineData(1ul, 0ul, V4FerryAction.Append)]
    public void ClassifyFerryLine_OrdersTheLog(ulong seq, ulong cursor, V4FerryAction expected) =>
        Assert.Equal(expected, V4Transport.ClassifyFerryLine(seq, cursor));

    [Fact]
    public void ScanFerryLog_SeedsCursorAndOwnAcks()
    {
        var logPath = Path.Combine(_dir, "log.v4.jsonl");
        File.WriteAllText(logPath,
            FactLog.EncodeFact(new Fact(1, Game.Oot, "THEIRS", 1)) + "\n"
            + FactLog.EncodeFact(new Fact(2, Game.Mm, "MINE", 2)) + "\n"
            + FactLog.EncodeFact(new Fact(2, Game.Oot, "PUBLISH_FORM")) + "\n"
            + "{\"v\":4,\"seq\":99,\"p\":2,\"g\":\"oot\",\"c\":\"TORN");

        var scan = V4Transport.ScanFerryLog(logPath, localPlayer: 2);
        Assert.Equal(2ul, scan.HighestSeq);
        Assert.Equal([V4Transport.NaturalKey(2, Game.Mm, "MINE")], scan.OwnKeys);

        var missing = V4Transport.ScanFerryLog(Path.Combine(_dir, "absent.jsonl"), 2);
        Assert.Equal(0ul, missing.HighestSeq);
        Assert.Empty(missing.OwnKeys);
    }

    [Fact]
    public void Sidecar_AddressingOrderingAndEmptyCoverage()
    {
        var local = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 2,
            PlayerCount = 3,
            BoolSettings = new Dictionary<string, bool> { ["trapsLink"] = true },
        };

        ItemPlacement Placement(string checkId, string itemId, ushort owner) =>
            new(new CheckRef(Game.Oot, checkId, "", [], null, "", null),
                new ItemRef(Game.Mm, itemId, itemId + " name"), null, owner, owner, false);

        Seed World(ushort playerId, params ItemPlacement[] placements) => new()
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = playerId,
            Placements = placements,
        };

        var worlds = new Dictionary<ushort, Seed>
        {
            [3] = World(3, Placement("W3_MINE", "MM_MASK", owner: 2)),
            [1] = World(1,
                Placement("W1_MINE", "MM_SONG", owner: 2),
                Placement("W1_THEIRS", "MM_JUNK", owner: 3),
                Placement("W1_BROADCAST", "MM_SOUL", owner: 0),
                Placement("W1_TRAP", "OOT_TRAP_ICE", owner: 3)),
            [2] = World(2, Placement("LOCAL_SKIPPED", "MM_X", owner: 2)),
        };

        Assert.Equal(
            """{"v":4,"localPlayer":2,"trapsLink":true,"coveredWorlds":[1,3],"placements":["""
            + """{"p":1,"g":"oot","c":"W1_MINE","itemGame":"mm","itemId":"MM_SONG","itemName":"MM_SONG name"},"""
            + """{"p":1,"g":"oot","c":"W1_BROADCAST","itemGame":"mm","itemId":"MM_SOUL","itemName":"MM_SOUL name"},"""
            + """{"p":1,"g":"oot","c":"W1_TRAP","itemGame":"mm","itemId":"OOT_TRAP_ICE","itemName":"OOT_TRAP_ICE name"},"""
            + """{"p":3,"g":"oot","c":"W3_MINE","itemGame":"mm","itemId":"MM_MASK","itemName":"MM_MASK name"}]}""",
            V4Transport.BuildForeignPlacementsSidecar(local, worlds));

        Assert.Equal(
            """{"v":4,"localPlayer":2,"trapsLink":true,"coveredWorlds":[],"placements":[]}""",
            V4Transport.BuildForeignPlacementsSidecar(local, new Dictionary<ushort, Seed>()));
    }
}
