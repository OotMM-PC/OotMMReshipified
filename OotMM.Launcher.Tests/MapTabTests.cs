using OotMM.Core;
using OotMM.Launcher.ViewModels;
using Xunit;

namespace OotMM.Launcher.Tests;

public sealed class MapTabTests : IDisposable
{
    private const string Index = """
        {
          "version": 1,
          "areas": [
            {"id": "oot-field", "game": "oot", "title": "Field", "image": "oot-field.jpg", "width": 800, "height": 600,
             "pins": [
               {"x": 0.50, "y": 0.50, "kind": "exit", "door": "Village exit", "entrances": ["OOT_VILLAGE_FROM_FIELD"]},
               {"x": 0.51, "y": 0.50, "kind": "exit", "door": "Temple exit", "entrances": ["OOT_TEMPLE"]},
               {"x": 0.50, "y": 0.51, "kind": "grotto", "door": "Field Grotto", "entrances": ["OOT_GROTTO_FIELD"]},
               {"x": 0.90, "y": 0.90, "kind": "exit", "door": "Shop exit", "entrances": []}
             ]},
            {"id": "oot-village", "game": "oot", "title": "Village", "image": "oot-village.jpg", "width": 640,
             "height": 480,
             "pins": [
               {"x": 0.2, "y": 0.8, "kind": "exit", "door": "Field exit", "entrances": ["OOT_FIELD_FROM_VILLAGE"]}
             ]}
          ],
          "interiors": {
            "oot-grotto-generic": {"image": "oot-grotto-generic.jpg", "width": 300, "height": 500,
                                   "exit": {"x": 0.5, "y": 0.9}}
          },
          "interiorEntrances": {"oot": {"63": "oot-grotto-generic"}, "mm": {}},
          "entrances": {
            "OOT_VILLAGE_FROM_FIELD": {"game": "oot", "id": 219, "type": "region", "from": "Field",
              "name": "Village", "area": "oot-village", "interior": "", "reverse": "OOT_FIELD_FROM_VILLAGE"},
            "OOT_FIELD_FROM_VILLAGE": {"game": "oot", "id": 385, "type": "region-exit", "from": "Village",
              "name": "Field", "area": "oot-field", "interior": "", "reverse": "OOT_VILLAGE_FROM_FIELD"},
            "OOT_TEMPLE": {"game": "oot", "id": 16, "type": "dungeon", "from": "Field", "name": "Temple",
              "area": "", "interior": "", "reverse": "OOT_FIELD_FROM_TEMPLE"},
            "OOT_FIELD_FROM_TEMPLE": {"game": "oot", "id": 521, "type": "dungeon-exit", "from": "Temple",
              "name": "Field", "area": "oot-field", "interior": "", "reverse": "OOT_TEMPLE"},
            "OOT_GROTTO_FIELD": {"game": "oot", "id": 4096, "type": "grotto", "from": "Field",
              "name": "Field Grotto", "area": "", "interior": "oot-grotto-generic", "reverse": ""}
          },
          "scenes": {"oot": {"81": "oot-field", "82": "oot-village"}, "mm": {}},
          "places": {"oot": {"63": "Grottos", "16": "Temple"}, "mm": {}}
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ootmm-maptab-" + Guid.NewGuid().ToString("N"));

    public MapTabTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "index.json"), Index);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private EntranceMap Map() =>
        new(new Seed
            {
                Format = Seed.CurrentFormat,
                GeneratorVersion = Seed.RequiredGeneratorVersion,
                SeedId = "S",
                PlayerId = 1,
                PlayerCount = 1,
                Entrances =
                [
                    new EntranceMapping(Game.Oot, "OOT_VILLAGE_FROM_FIELD", 219, Game.Oot, "OOT_TEMPLE", 16),
                    new EntranceMapping(Game.Oot, "OOT_FIELD_FROM_TEMPLE", 521, Game.Oot, "OOT_FIELD_FROM_VILLAGE", 385),
                    new EntranceMapping(Game.Oot, "OOT_TEMPLE", 16, Game.Oot, "OOT_VILLAGE_FROM_FIELD", 219),
                ],
            },
            EntranceMapPack.Load(_directory)!);

    private (MapTabViewModel Tab, EntranceMap Map) Tab()
    {
        var map = Map();
        var tab = new MapTabViewModel();
        tab.Attach(map);
        tab.LayoutPins(400, 300);
        return (tab, map);
    }

    [Fact]
    public void WithoutAPackTheTabSaysSoAndStaysEmpty()
    {
        var tab = new MapTabViewModel();
        tab.Attach(null);

        Assert.False(tab.Available);
        Assert.Empty(tab.Pins);
    }

    [Fact]
    public void TrackedPinsAreNumberedAndNeverOverlap()
    {
        var (tab, _) = Tab();
        tab.ShowFixed = true;

        Assert.Equal("Field", tab.Title);
        Assert.Equal("0 of 2 entrances found here", tab.CountText);
        Assert.Equal(["1", "2", "", ""], tab.Pins.Select(p => p.Number));
        Assert.Equal(["open", "open", "hole", "fixed"], tab.Pins.Select(p => p.Style));
        for (var i = 0; i < tab.Pins.Count; i++)
        {
            for (var j = i + 1; j < tab.Pins.Count; j++)
            {
                var gap = Math.Sqrt(Math.Pow(tab.Pins[i].X - tab.Pins[j].X, 2) + Math.Pow(tab.Pins[i].Y - tab.Pins[j].Y, 2));
                var radii = (tab.Pins[i].Number.Length > 0 ? 10 : 5) + (tab.Pins[j].Number.Length > 0 ? 10 : 5);
                Assert.True(gap >= radii + 2, $"pins {i} and {j} are {gap:F1} apart");
            }
        }

        Assert.Contains(tab.Pins, p => p.Displaced);
    }

    [Fact]
    public void FixedExitsStayOffTheMapUntilAsked()
    {
        var (tab, _) = Tab();

        Assert.Equal(2, tab.Pins.Count);
        Assert.DoesNotContain(tab.Rows, r => r.Text == "Shop exit");

        tab.ShowFixed = true;

        Assert.Equal(4, tab.Pins.Count);
        Assert.Contains(tab.Rows, r => r.Text == "Shop exit" && r.Detail == "→ Shop");
    }

    [Fact]
    public void WalkingThroughAnEntranceShowsItsDestinationAndFollowsLink()
    {
        var (tab, map) = Tab();
        tab.Update(new MapPosition(Game.Oot, 82, 385, "", ""));
        Assert.Equal("Village", tab.Title);
        Assert.True(tab.LinkIsHere);

        map.Discover("OOT_FIELD_FROM_TEMPLE");
        map.Discover("OOT_TEMPLE");
        tab.Update(new MapPosition(Game.Oot, 81, 219, "OOT_FIELD_FROM_TEMPLE", "OOT_TEMPLE"));

        Assert.Equal("Field", tab.Title);
        Assert.Equal("2 of 2 entrances found here", tab.CountText);
        Assert.True(tab.CountComplete);
        var temple = tab.Rows.Single(r => r.Text == "Temple exit");
        Assert.Equal("→ Village", temple.Detail);
        Assert.Equal("came in here", temple.Pill);
        Assert.Equal("oot-village", temple.Target);
        Assert.True(tab.Pins.Single(p => p.PinIndex == 1).Came);
        Assert.True(tab.Rows.Single(r => r.Text == "Village exit").Dungeon);
    }

    [Fact]
    public void BrowsingAwayStopsFollowingAndOffersTheWayBack()
    {
        var (tab, map) = Tab();
        map.Discover("OOT_TEMPLE");
        tab.Update(new MapPosition(Game.Oot, 81, 219, "", ""));

        tab.ActivatePin(1);

        Assert.Equal("Village", tab.Title);
        Assert.False(tab.Follow);
        Assert.False(tab.LinkIsHere);
        Assert.Equal("Link is in Field · show", tab.Notice);
        Assert.True(tab.NoticeJumps);

        tab.ShowLink();

        Assert.Equal("Field", tab.Title);
        Assert.Equal("", tab.Notice);
    }

    [Fact]
    public void AGrottoShowsItsInteriorWithTheWayOut()
    {
        var (tab, _) = Tab();
        tab.Update(new MapPosition(Game.Oot, 81, 219, "", ""));

        tab.Update(new MapPosition(Game.Oot, 62, 63, "OOT_GROTTO_FIELD", ""));

        Assert.True(tab.ShowingInterior);
        Assert.Equal("Field Grotto", tab.Title);
        Assert.EndsWith("oot-grotto-generic.jpg", tab.ImageSource);
        var pin = Assert.Single(tab.Pins);
        Assert.Equal(MapTabViewModel.WayOutPin, pin.PinIndex);
        Assert.Equal("exit", pin.Style);
        Assert.Contains(tab.Rows, r => r.Kind == MapTabViewModel.WayOutRow && r.Detail == "→ Field, Field Grotto");

        tab.ActivateRow(tab.Rows.ToList().FindIndex(r => r.Kind == MapTabViewModel.ShowOutsideRow));

        Assert.False(tab.ShowingInterior);
        Assert.Equal("Field", tab.Title);
        Assert.Equal("Link is inside Field Grotto · show", tab.Notice);
        tab.ShowFixed = true;
        Assert.Equal("went in", tab.Rows.Single(r => r.Text == "Field Grotto").Pill);
    }

    [Fact]
    public void APlaceWithoutAPictureKeepsTheAreaOutsideAndNamesWhereLinkIs()
    {
        var (tab, map) = Tab();
        map.Discover("OOT_VILLAGE_FROM_FIELD");

        tab.Update(new MapPosition(Game.Oot, 3, 16, "OOT_VILLAGE_FROM_FIELD", "OOT_FIELD_FROM_TEMPLE"));

        Assert.False(tab.ShowingInterior);
        Assert.Equal("Field", tab.Title);
        Assert.Equal("Link is inside Temple", tab.Notice);
        Assert.False(tab.NoticeJumps);
        Assert.Equal("went in", tab.Rows.Single(r => r.Text == "Village exit").Pill);
    }

    [Fact]
    public void EntrancesWithoutAPinGetTheirOwnListOnlyArea()
    {
        var (tab, map) = Tab();
        var index = tab.Areas.ToList().FindIndex(a => a.Title == "Dungeons and others");
        Assert.Equal("0/1", tab.Areas[index].CountText);

        tab.SelectArea(index);

        Assert.False(tab.HasPicture);
        Assert.Empty(tab.Pins);
        Assert.Equal("0 of 1 entrances found", tab.CountText);
        Assert.Contains(tab.Rows, r => r.Text == "Temple: Field exit" && r.Detail == "not explored yet");

        map.Discover("OOT_FIELD_FROM_TEMPLE");
        tab.Update(null);

        Assert.Contains(tab.Rows, r => r.Text == "Temple: Field exit" && r.Detail == "→ Field" && r.Target == "oot-field");
    }

    [Fact]
    public void TurningTheMapOffHidesItsTabAndLeavesIt()
    {
        var settings = new OotMM.Launcher.Models.UiSettings();
        var runtime = RuntimeViewModel.CreatePreview(settings, () => { }, _ => { }, _ => { }, 1.0);
        Assert.True(runtime.ShowMapTab);
        runtime.SelectedTabIndex = 5;
        Assert.Same(runtime.Map, runtime.CurrentTab);

        runtime.Options.EntranceMap = false;

        Assert.False(settings.EntranceMap);
        Assert.False(runtime.ShowMapTab);
        Assert.Same(runtime.Tracker, runtime.CurrentTab);
        runtime.SelectedTabIndex = 5;
        Assert.Same(runtime.Tracker, runtime.CurrentTab);

        runtime.Options.EntranceMap = true;

        Assert.True(runtime.ShowMapTab);
    }
}
