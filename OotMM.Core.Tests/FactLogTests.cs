using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class FactLogTests
{
    [Fact]
    public void EncodeFact_MatchesWireShape()
    {
        Assert.Equal(
            """{"v":4,"seq":7,"p":2,"g":"oot","c":"OOT_ZELDAS_LETTER"}""",
            FactLog.EncodeFact(new Fact(2, Game.Oot, "OOT_ZELDAS_LETTER", 7)));

        // Publish form: seq omitted entirely, not written as 0.
        Assert.Equal(
            """{"v":4,"p":1,"g":"mm","c":"MM_CHECK"}""",
            FactLog.EncodeFact(new Fact(1, Game.Mm, "MM_CHECK")));
    }

    [Fact]
    public void FactAndAck_RoundTrip()
    {
        var fact = new Fact(3, Game.Mm, "MM_CLOCK_TOWN_POSTBOX", 42);
        Assert.Equal(fact, FactLog.DecodeFact(FactLog.EncodeFact(fact)));
        Assert.Equal(fact, FactLog.DecodeAck(FactLog.EncodeAck(fact)));
    }

    [Fact]
    public void Decoders_NeverCrossDecode()
    {
        var factLine = FactLog.EncodeFact(new Fact(1, Game.Oot, "C", 5));
        var ackLine = FactLog.EncodeAck(new Fact(1, Game.Oot, "C", 5));
        var catchUpLine = FactLog.EncodeCatchUp(9);
        var liveLine = FactLog.EncodeLive();

        Assert.Null(FactLog.DecodeFact(ackLine));
        Assert.Null(FactLog.DecodeFact(catchUpLine));
        Assert.Null(FactLog.DecodeFact(liveLine));
        Assert.Null(FactLog.DecodeAck(factLine));
        Assert.Null(FactLog.DecodeCatchUp(factLine));
        Assert.False(FactLog.DecodeLive(factLine));

        Assert.Equal(9ul, FactLog.DecodeCatchUp(catchUpLine));
        Assert.True(FactLog.DecodeLive(liveLine));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"v":3,"p":1,"g":"oot","c":"C"}""")]
    [InlineData("""{"v":4,"p":0,"g":"oot","c":"C"}""")]
    [InlineData("""{"v":4,"p":70000,"g":"oot","c":"C"}""")]
    [InlineData("""{"v":4,"p":1,"g":"zelda","c":"C"}""")]
    [InlineData("""{"v":4,"p":1,"g":"oot","c":""}""")]
    [InlineData("""{"v":4,"p":1,"g":"oot"}""")]
    [InlineData("""{"v":4,"seq":1.5,"p":1,"g":"oot","c":"C"}""")]
    [InlineData("""{"v":4,"seq":-3,"p":1,"g":"oot","c":"C"}""")]
    public void DecodeFact_RejectsMalformedLines(string line) =>
        Assert.Null(FactLog.DecodeFact(line));

    [Fact]
    public void DecodeFact_ToleratesUnknownFieldsAndStringNumbers()
    {
        var fact = FactLog.DecodeFact(
            """{"v":4,"seq":"12","p":"2","g":"OoT","c":"C","future":true}""");
        Assert.Equal(new Fact(2, Game.Oot, "C", 12), fact);
    }

    [Fact]
    public void DeliveryKeyFor_MatchesCppFormat() =>
        Assert.Equal(
            "v4:p2:oot:OOT_CHECK:to:mm",
            FactLog.DeliveryKeyFor(new Fact(2, Game.Oot, "OOT_CHECK", 9), Game.Mm));
}

public class ForeignPlacementTableTests
{
    private static ItemPlacement Placement(
        string checkId, string itemId, ushort owner,
        Game checkGame = Game.Oot, string name = "", string[]? aliases = null) =>
        new(new CheckRef(checkGame, checkId, name, aliases ?? [], null, "", null),
            new ItemRef(Game.Oot, itemId, itemId), null, owner, owner, false);

    private static Seed MakeSeed(
        ushort playerId, IReadOnlyList<ItemPlacement> placements, bool trapsLink = false) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "S",
        PlayerId = playerId,
        PlayerCount = 2,
        Placements = placements,
        BoolSettings = new Dictionary<string, bool> { ["trapsLink"] = trapsLink },
    };

    [Fact]
    public void AddressingRules_OwnerBroadcastAndLinkedTrap()
    {
        var world = MakeSeed(1,
        [
            Placement("MINE", "OOT_SWORD", owner: 2),
            Placement("THEIRS", "OOT_SHIELD", owner: 1),
            Placement("EVERYONE", "OOT_SOUL", owner: 0),
            Placement("TRAP", "OOT_TRAP_ICE", owner: 1),
        ]);

        var local = MakeSeed(2, [], trapsLink: false);
        var table = new ForeignPlacementTable(local);
        table.AddManifestWorld(1, world, localPlayer: 2);
        Assert.NotNull(table.Find(1, Game.Oot, "MINE"));
        Assert.Null(table.Find(1, Game.Oot, "THEIRS"));
        Assert.NotNull(table.Find(1, Game.Oot, "EVERYONE"));
        Assert.Null(table.Find(1, Game.Oot, "TRAP")); // trapsLink off

        var linked = new ForeignPlacementTable(MakeSeed(2, [], trapsLink: true));
        linked.AddManifestWorld(1, world, localPlayer: 2);
        Assert.True(linked.TrapsLink);
        Assert.NotNull(linked.Find(1, Game.Oot, "TRAP"));
    }

    [Fact]
    public void LocalWorldIsAlwaysCoveredAndIndexed()
    {
        var local = MakeSeed(2, [Placement("SELF", "MM_MASK", owner: 2)]);
        var table = new ForeignPlacementTable(local);

        Assert.True(table.CoversWorld(2));
        Assert.False(table.CoversWorld(1));
        Assert.NotNull(table.Find(2, Game.Oot, "SELF"));
    }

    [Fact]
    public void SidecarCoverage_OldAndNewFormats()
    {
        var local = MakeSeed(2, []);
        var placements = new[]
        {
            new ForeignPlacement(1, Game.Mm, "MM_CHECK", new ItemRef(Game.Oot, "OOT_X", "X")),
        };

        var oldFormat = new ForeignPlacementTable(local, placements);
        Assert.True(oldFormat.CoversWorld(1));
        Assert.NotNull(oldFormat.Find(1, Game.Mm, "MM_CHECK"));

        // World 3 addressed us nothing but is explicitly covered; world 0 never registers.
        var newFormat = new ForeignPlacementTable(local, placements, [3, 0]);
        Assert.True(newFormat.CoversWorld(3));
        Assert.False(newFormat.CoversWorld(0));
        Assert.Equal(new ushort[] { 1, 2, 3 }, newFormat.CoveredWorlds);
    }

    [Fact]
    public void NamesAliasesAndFirstWriterWins()
    {
        var world = MakeSeed(1,
        [
            Placement("CANONICAL", "OOT_FIRST", owner: 2, name: "Pretty Name", aliases: ["ALT"]),
            Placement("CANONICAL", "OOT_SECOND", owner: 2),
        ]);
        var table = new ForeignPlacementTable(MakeSeed(2, []));
        table.AddManifestWorld(1, world, localPlayer: 2);

        Assert.Equal("OOT_FIRST", table.Find(1, Game.Oot, "CANONICAL")!.Id);
        Assert.Equal("OOT_FIRST", table.Find(1, Game.Oot, "Pretty Name")!.Id);
        Assert.Equal("OOT_FIRST", table.Find(1, Game.Oot, "ALT")!.Id);
    }
}
