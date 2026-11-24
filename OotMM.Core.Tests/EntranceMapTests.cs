using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class EntranceMapTests : IDisposable
{
    private const string Index = """
        {
          "version": 1,
          "areas": [
            {"id": "oot-field", "game": "oot", "title": "Field", "image": "oot-field.jpg", "width": 800, "height": 600,
             "pins": [
               {"x": 0.1, "y": 0.2, "kind": "exit", "door": "Village entrance",
                "entrances": ["OOT_VILLAGE_FROM_FIELD"]},
               {"x": 0.5, "y": 0.5, "kind": "exit", "door": "Temple entrance", "entrances": ["OOT_TEMPLE"]},
               {"x": 0.7, "y": 0.3, "kind": "grotto", "door": "Field Grotto", "entrances": ["OOT_GROTTO_FIELD"]},
               {"x": 0.9, "y": 0.9, "kind": "exit", "door": "Shop entrance", "entrances": []}
             ]},
            {"id": "oot-village", "game": "oot", "title": "Village", "image": "oot-village.jpg", "width": 640,
             "height": 480,
             "pins": [
               {"x": 0.2, "y": 0.8, "kind": "exit", "door": "Field entrance",
                "entrances": ["OOT_FIELD_FROM_VILLAGE", "OOT_FIELD_FROM_VILLAGE_NIGHT"]}
             ]}
          ],
          "interiors": {
            "oot-grotto-generic": {"image": "oot-grotto-generic.jpg", "width": 300, "height": 500,
                                   "exit": {"x": 0.5, "y": 0.9}}
          },
          "interiorEntrances": {"oot": {"63": "oot-grotto-generic"}, "mm": {"5312": "oot-grotto-generic"}},
          "entrances": {
            "OOT_VILLAGE_FROM_FIELD": {"game": "oot", "id": 219, "type": "region", "from": "Field",
              "name": "Village", "area": "oot-village", "interior": "", "reverse": "OOT_FIELD_FROM_VILLAGE"},
            "OOT_FIELD_FROM_VILLAGE": {"game": "oot", "id": 385, "type": "region-exit", "from": "Village",
              "name": "Field", "area": "oot-field", "interior": "", "reverse": "OOT_VILLAGE_FROM_FIELD"},
            "OOT_FIELD_FROM_VILLAGE_NIGHT": {"game": "oot", "id": 386, "type": "region-exit", "from": "Village",
              "name": "Field", "area": "oot-field", "interior": "", "reverse": "OOT_VILLAGE_FROM_FIELD"},
            "OOT_TEMPLE": {"game": "oot", "id": 16, "type": "dungeon", "from": "Field", "name": "Temple",
              "area": "", "interior": "", "reverse": "OOT_FIELD_FROM_TEMPLE"},
            "OOT_FIELD_FROM_TEMPLE": {"game": "oot", "id": 521, "type": "dungeon-exit", "from": "Temple",
              "name": "Field", "area": "oot-field", "interior": "", "reverse": "OOT_TEMPLE"},
            "OOT_CAVE": {"game": "oot", "id": 4, "type": "dungeon", "from": "Mountain", "name": "Cave",
              "area": "", "interior": "", "reverse": "OOT_MOUNTAIN_FROM_CAVE"},
            "OOT_MOUNTAIN_FROM_CAVE": {"game": "oot", "id": 578, "type": "dungeon-exit", "from": "Cave",
              "name": "Mountain", "area": "", "interior": "", "reverse": "OOT_CAVE"},
            "OOT_GROTTO_FIELD": {"game": "oot", "id": 4096, "type": "grotto", "from": "Field",
              "name": "Field Grotto", "area": "", "interior": "oot-grotto-generic", "reverse": ""},
            "OOT_SPAWN_CHILD": {"game": "oot", "id": 187, "type": "spawn-child", "from": "Spawn", "name": "House",
              "area": "", "interior": "", "reverse": ""},
            "MM_SHOP": {"game": "mm", "id": 25088, "type": "region", "from": "Town", "name": "Shop",
              "area": "", "interior": "", "reverse": ""},
            "MM_GROTTO_GRASS": {"game": "mm", "id": 65537, "type": "grotto", "from": "Town", "name": "Grass Grotto",
              "area": "", "interior": "", "reverse": ""}
          },
          "scenes": {"oot": {"81": "oot-field", "82": "oot-village"}, "mm": {}},
          "places": {"oot": {"1000": "Fishing Pond"}, "mm": {"54": "Clock Tower Interior"}}
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ootmm-map-" + Guid.NewGuid().ToString("N"));

    public EntranceMapTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "index.json"), Index);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private EntranceMapPack Pack() => EntranceMapPack.Load(_directory)!;

    private static Seed SeedWith(bool decoupled, params (string From, string To)[] entrances) =>
        new()
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 1,
            PlayerCount = 1,
            BoolSettings = new Dictionary<string, bool> { ["erDecoupled"] = decoupled },
            Entrances = [.. entrances.Select(e => new EntranceMapping(Game.Oot, e.From, null, Game.Oot, e.To, null))],
        };

    private static Seed Shuffled(bool decoupled = false) =>
        SeedWith(decoupled,
                 ("OOT_VILLAGE_FROM_FIELD", "OOT_CAVE"),
                 ("OOT_MOUNTAIN_FROM_CAVE", "OOT_FIELD_FROM_VILLAGE"),
                 ("OOT_FIELD_FROM_VILLAGE", "OOT_FIELD_FROM_TEMPLE"),
                 ("OOT_TEMPLE", "OOT_TEMPLE"),
                 ("OOT_SPAWN_CHILD", "OOT_CAVE"));

    [Fact]
    public void MissingOrBrokenPacksLoadAsNothing()
    {
        Assert.Null(EntranceMapPack.Load(Path.Combine(_directory, "absent")));
        File.WriteAllText(Path.Combine(_directory, "index.json"), "{\"version\": 2}");
        Assert.Null(EntranceMapPack.Load(_directory));
        File.WriteAllText(Path.Combine(_directory, "index.json"), "not json");
        Assert.Null(EntranceMapPack.Load(_directory));
    }

    [Fact]
    public void TheShippedPackOnlyPointsAtThingsItHolds()
    {
        var pack = EntranceMapPack.LoadInstalled();

        Assert.NotNull(pack);
        Assert.NotEmpty(pack.Areas);
        Assert.All(pack.Areas.SelectMany(area => area.Pins).SelectMany(pin => pin.Entrances),
                   key => Assert.Contains(key, pack.Entrances.Keys));
        Assert.All(pack.Entrances.Values, entrance =>
        {
            Assert.True(entrance.Reverse.Length == 0 || pack.Entrances.ContainsKey(entrance.Reverse), entrance.Key);
            Assert.True(entrance.Area.Length == 0 || pack.FindArea(entrance.Area) is not null, entrance.Key);
            Assert.True(entrance.Interior.Length == 0 || pack.Interiors.ContainsKey(entrance.Interior), entrance.Key);
            Assert.Same(entrance, pack.FindEntrance(entrance.Game, entrance.NativeId));
        });
    }

    [Fact]
    public void PackAnswersSceneEntranceAndPlaceLookups()
    {
        var pack = Pack();

        Assert.Equal("oot-village", pack.AreaOfScene(Game.Oot, 82)?.Id);
        Assert.Null(pack.AreaOfScene(Game.Mm, 82));
        Assert.Equal("OOT_TEMPLE", pack.FindEntrance(Game.Oot, 16)?.Key);
        Assert.Equal("oot-grotto-generic", pack.InteriorOfEntrance(Game.Oot, 63)?.Id);
        Assert.Null(pack.InteriorOfEntrance(Game.Oot, 64));
        Assert.Equal("Temple", pack.PlaceName(Game.Oot, 16));
        Assert.Equal("Fishing Pond", pack.PlaceName(Game.Oot, 1000));
        Assert.Equal("Clock Tower Interior", pack.PlaceName(Game.Mm, 54 << 9 | 0x10));
        Assert.Equal("Shop", pack.PlaceName(Game.Mm, 25088 | 2));
        Assert.Equal("oot-grotto-generic", pack.InteriorOfEntrance(Game.Mm, 5312 | 1)?.Id);
        Assert.Equal("MM_GROTTO_GRASS", pack.FindEntrance(Game.Mm, 65537)?.Key);
        Assert.Null(pack.FindEntrance(Game.Mm, 25088 | 2));
        Assert.Equal(Path.Combine(_directory, "oot-field.jpg"), pack.ImagePath(pack.Areas[0].Image));
    }

    [Fact]
    public void OnlyRowsThatChangeTheirDestinationAreTracked()
    {
        var pins = new EntranceMap(Shuffled(), Pack()).PinsOf(Pack().FindArea("oot-field")!);

        Assert.Equal(MapPinState.Unexplored, pins[0].State);
        Assert.Equal("", pins[0].Destination);
        Assert.Equal(MapPinState.Fixed, pins[1].State);
        Assert.Equal("Temple", pins[1].Destination);
        Assert.Equal(MapPinState.Fixed, pins[2].State);
        Assert.Equal("oot-grotto-generic", pins[2].DestinationInterior);
        Assert.Equal(MapPinState.Fixed, pins[3].State);
        Assert.Equal("Shop", pins[3].Destination);
    }

    [Fact]
    public void FindingAnEntranceShowsWhereItLeadsAndMarksItsReverse()
    {
        var pack = Pack();
        var map = new EntranceMap(Shuffled(), pack);

        Assert.True(map.Discover("OOT_VILLAGE_FROM_FIELD"));
        Assert.False(map.Discover("OOT_VILLAGE_FROM_FIELD"));
        Assert.False(map.Discover("OOT_TEMPLE"));
        Assert.False(map.Discover("OOT_UNKNOWN"));

        var field = map.PinsOf(pack.FindArea("oot-field")!)[0];
        Assert.Equal(MapPinState.Found, field.State);
        Assert.Equal("Cave", field.Destination);
        Assert.True(field.LeadsToDungeon);
        Assert.Contains("OOT_MOUNTAIN_FROM_CAVE", map.Discovered);
        Assert.Equal(MapPinState.Unexplored, map.PinsOf(pack.FindArea("oot-village")!)[0].State);
    }

    [Fact]
    public void DecoupledSeedsOnlyMarkTheSideWalkedThrough()
    {
        var map = new EntranceMap(Shuffled(decoupled: true), Pack());

        map.Discover("OOT_VILLAGE_FROM_FIELD");

        Assert.Equal(["OOT_VILLAGE_FROM_FIELD"], map.Discovered);
    }

    [Fact]
    public void APinFollowsWhicheverOfItsEntrancesTheSeedShuffles()
    {
        var pack = Pack();
        var map = new EntranceMap(SeedWith(false, ("OOT_FIELD_FROM_VILLAGE_NIGHT", "OOT_FIELD_FROM_TEMPLE")), pack);

        Assert.Equal("OOT_FIELD_FROM_VILLAGE_NIGHT", map.PinsOf(pack.FindArea("oot-village")!)[0].Key);
        map.Discover("OOT_FIELD_FROM_VILLAGE_NIGHT");

        var pin = map.PinsOf(pack.FindArea("oot-village")!)[0];
        Assert.Equal(MapPinState.Found, pin.State);
        Assert.Equal("oot-field", pin.DestinationArea);
        Assert.False(pin.LeadsToDungeon);
    }

    [Fact]
    public void ShuffledEntrancesWithoutAPinAreListedApartFromSpawns()
    {
        var map = new EntranceMap(Shuffled(), Pack());

        var listed = map.Unpinned(Game.Oot);

        Assert.Equal(["OOT_MOUNTAIN_FROM_CAVE"], listed.Select(e => e.Key));
        Assert.Equal("Cave: Mountain entrance", listed[0].Door);
        Assert.Equal(-1, listed[0].PinIndex);
        Assert.Empty(map.Unpinned(Game.Mm));
    }

    [Fact]
    public void ArrivingAfterAnEntranceRecordsBothSidesOfIt()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Arrive(Game.Oot, 81, 205);

        map.Leave("OOT_VILLAGE_FROM_FIELD");
        map.Arrive(Game.Oot, 81, 4);
        map.Arrive(Game.Oot, 4, 4);

        var arrived = new MapPosition(Game.Oot, 4, 4, "OOT_VILLAGE_FROM_FIELD", "OOT_MOUNTAIN_FROM_CAVE");
        Assert.Equal(arrived, map.Position);
        map.Arrive(Game.Oot, 4, 5);
        Assert.Equal(arrived with { Entrance = 5 }, map.Position);
    }

    [Fact]
    public void ArrivingWithoutAReportFromTheOldSceneStillRecordsTheEntrance()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Arrive(Game.Oot, 81, 205);

        map.Leave("OOT_VILLAGE_FROM_FIELD");
        map.Arrive(Game.Oot, 4, 4);

        Assert.Equal(new MapPosition(Game.Oot, 4, 4, "OOT_VILLAGE_FROM_FIELD", "OOT_MOUNTAIN_FROM_CAVE"),
                     map.Position);
    }

    [Fact]
    public void TheArrivingReportMayCarryADifferentEntranceThanTheOneFromTheOldScene()
    {
        var map = new EntranceMap(SeedWith(false, ("OOT_TEMPLE", "OOT_VILLAGE_FROM_FIELD")), Pack());
        map.Arrive(Game.Oot, 81, 205);

        map.Leave("OOT_TEMPLE");
        map.Arrive(Game.Oot, 81, 219);
        Assert.Equal("", map.Position?.TakenKey);
        map.Arrive(Game.Oot, 82, 220);

        Assert.Equal(new MapPosition(Game.Oot, 82, 220, "OOT_TEMPLE", "OOT_FIELD_FROM_VILLAGE"), map.Position);
    }

    [Fact]
    public void AnEntranceBackIntoTheSameSceneIsNotCarriedToTheNextPlace()
    {
        var map = new EntranceMap(SeedWith(false, ("OOT_VILLAGE_FROM_FIELD", "OOT_FIELD_FROM_TEMPLE")), Pack());
        map.Arrive(Game.Oot, 81, 205);
        map.Leave("OOT_VILLAGE_FROM_FIELD");
        map.Arrive(Game.Oot, 81, 521);
        Assert.Equal(new MapPosition(Game.Oot, 81, 521, "OOT_VILLAGE_FROM_FIELD", "OOT_TEMPLE"), map.Position);

        map.Arrive(Game.Oot, 82, 219);

        Assert.Equal(new MapPosition(Game.Oot, 82, 219, "", ""), map.Position);
    }

    [Fact]
    public void AnEntranceThatDoesNotLeadWhereLinkTurnsUpIsDropped()
    {
        var map = new EntranceMap(SeedWith(false, ("OOT_VILLAGE_FROM_FIELD", "OOT_FIELD_FROM_TEMPLE")), Pack());
        map.Arrive(Game.Oot, 81, 521);
        map.Leave("OOT_VILLAGE_FROM_FIELD");

        map.Arrive(Game.Oot, 82, 219);
        map.Arrive(Game.Oot, 81, 521);

        Assert.Equal(new MapPosition(Game.Oot, 81, 521, "", ""), map.Position);
    }

    [Fact]
    public void LeavingAPlaceWithoutAPictureArrivesWhereLinkWentIn()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Leave("OOT_GROTTO_FIELD");
        map.Arrive(Game.Oot, 62, 63);

        map.Arrive(Game.Oot, 81, 205);

        Assert.Equal(new MapPosition(Game.Oot, 81, 205, "", "OOT_GROTTO_FIELD"), map.Position);
        map.Arrive(Game.Oot, 82, 219);
        Assert.Equal(new MapPosition(Game.Oot, 82, 219, "", ""), map.Position);
    }

    [Fact]
    public void WarpingOutOfAPlaceWithoutAPictureArrivesNowhereInParticular()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Leave("OOT_GROTTO_FIELD");
        map.Arrive(Game.Oot, 62, 63);

        map.Arrive(Game.Oot, 82, 219);

        Assert.Equal(new MapPosition(Game.Oot, 82, 219, "", ""), map.Position);
    }

    [Fact]
    public void ResetForgetsWhatWasFoundAndWhereLinkIs()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Discover("OOT_VILLAGE_FROM_FIELD");
        map.Arrive(Game.Oot, 81, 205);
        map.Leave("OOT_VILLAGE_FROM_FIELD");

        map.Reset();
        Assert.Null(map.Position);
        map.Arrive(Game.Oot, 4, 4);

        Assert.Empty(map.Discovered);
        Assert.Equal(new MapPosition(Game.Oot, 4, 4, "", ""), map.Position);
    }

    [Fact]
    public void RestoreReplacesWhatWasFound()
    {
        var map = new EntranceMap(Shuffled(), Pack());
        map.Discover("OOT_FIELD_FROM_VILLAGE");

        map.Restore(["OOT_VILLAGE_FROM_FIELD", "OOT_UNKNOWN"]);

        Assert.Equal(["OOT_MOUNTAIN_FROM_CAVE", "OOT_VILLAGE_FROM_FIELD"], map.Discovered.Order());
    }
}
