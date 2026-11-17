using System.Collections.ObjectModel;
using OotMM.Core;

namespace OotMM.Launcher.ViewModels;

public sealed record MapAreaItem(
    bool IsHeader, string Id, string Title, string Tag, string CountText, bool Complete, bool IsHere, bool Selected);

public sealed record MapPinItem(
    int PinIndex, double X, double Y, double AnchorX, double AnchorY, bool Displaced, string Number, string Style,
    bool Dungeon, bool Grotto, bool Came, string Tip);

public sealed record MapRowItem(
    int Kind, string Text, string Detail, string Tag, string Number, string Style, bool Dungeon, bool Grotto,
    string Pill, int PinIndex, string Target);

/// <summary>The sidebar's Map tab: one area or grotto interior at a time, its entrance pins and their list.</summary>
public sealed class MapTabViewModel : ViewModelBase
{
    public const int HeaderRow = 0;
    public const int EntranceRow = 1;
    public const int WayOutRow = 2;
    public const int ShowOutsideRow = 3;
    public const int WayOutPin = -2;

    private const string UnpinnedPrefix = "other-";
    private const double TrackedRadius = 10;
    private const double FixedRadius = 5;

    private EntranceMap? _map;
    private MapPosition? _position;
    private string _viewId = "";
    private bool _follow = true;
    private bool _showFixed;
    private bool _expanded;
    private bool _showOutside;
    private double _mapWidth;
    private double _mapHeight;
    private string _linkAreaId = "";
    private bool _insideView;
    private IReadOnlyList<MapEntranceView> _views = [];

    public bool Available => _map is not null;

    public ObservableCollection<MapAreaItem> Areas { get; private set; } = [];

    public ObservableCollection<MapPinItem> Pins { get; private set; } = [];

    public ObservableCollection<MapRowItem> Rows { get; private set; } = [];

    public string Title { get; private set; } = "";

    public string GameTag { get; private set; } = "OOT";

    public bool LinkIsHere { get; private set; }

    public bool HasPicture { get; private set; }

    public bool ShowingInterior => _insideView;

    public string ImageSource { get; private set; } = "";

    public int ImageWidth { get; private set; } = 1;

    public int ImageHeight { get; private set; } = 1;

    public string CountText { get; private set; } = "";

    public bool CountComplete { get; private set; }

    public string Notice { get; private set; } = "";

    public bool NoticeJumps { get; private set; }

    public bool Follow
    {
        get => _follow;
        set
        {
            if (Set(ref _follow, value) && value)
            {
                _showOutside = false;
                Refresh();
            }
        }
    }

    public bool ShowFixed
    {
        get => _showFixed;
        set
        {
            if (Set(ref _showFixed, value))
            {
                Refresh();
            }
        }
    }

    public bool Expanded
    {
        get => _expanded;
        set => Set(ref _expanded, value);
    }

    public void Attach(EntranceMap? map)
    {
        _map = map;
        _viewId = "";
        Raise(nameof(Available));
        Refresh();
    }

    /// <summary>Takes the player's latest position and redraws, also picking up newly found entrances.</summary>
    public void Update(MapPosition? position)
    {
        if (_position != position)
        {
            _showOutside = false;
        }

        _position = position;
        Refresh();
    }

    public void LayoutPins(double width, double height)
    {
        if (Math.Abs(width - _mapWidth) < 0.5 && Math.Abs(height - _mapHeight) < 0.5)
        {
            return;
        }

        _mapWidth = width;
        _mapHeight = height;
        RebuildPins();
    }

    public void SelectArea(int index)
    {
        if (index < 0 || index >= Areas.Count || Areas[index].IsHeader)
        {
            return;
        }

        Show(Areas[index].Id);
    }

    public void ActivatePin(int pinIndex)
    {
        var view = _insideView ? null : _views.FirstOrDefault(v => v.PinIndex == pinIndex);
        if (view is { State: not MapPinState.Unexplored, DestinationArea.Length: > 0 }
            && view.DestinationArea != _viewId)
        {
            Show(view.DestinationArea);
        }
    }

    public void ActivateRow(int index)
    {
        if (index < 0 || index >= Rows.Count)
        {
            return;
        }

        var row = Rows[index];
        if (row.Kind == ShowOutsideRow)
        {
            _showOutside = true;
            Refresh();
        }
        else if (row.Kind == EntranceRow && row.Target.Length > 0)
        {
            Show(row.Target);
        }
    }

    /// <summary>Brings the view back to wherever the player is.</summary>
    public void ShowLink()
    {
        if (_linkAreaId.Length == 0)
        {
            return;
        }

        _viewId = _linkAreaId;
        _showOutside = false;
        Refresh();
    }

    private void Show(string areaId)
    {
        _viewId = areaId;
        _showOutside = false;
        if (areaId != _linkAreaId)
        {
            Set(ref _follow, false, nameof(Follow));
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_map is null)
        {
            return;
        }

        var pack = _map.Pack;
        var here = _position is null ? null : pack.AreaOfScene(_position.Game, _position.Scene);
        var interior = _position is null || here is not null
            ? null : pack.InteriorOfEntrance(_position.Game, _position.Entrance);
        var outside = here ?? (_position is { TakenKey.Length: > 0 }
            ? pack.Areas.FirstOrDefault(a => a.Pins.Any(p => p.Entrances.Contains(_position.TakenKey))) : null);
        var insideName = _position is null || here is not null ? ""
            : _position.TakenKey.Length > 0 && _map.DestinationOf(_position.TakenKey) is { Length: > 0 } named ? named
            : pack.PlaceName(_position.Game, _position.Entrance);
        _linkAreaId = outside?.Id ?? "";

        if (_follow && _linkAreaId.Length > 0)
        {
            _viewId = _linkAreaId;
        }

        if (_viewId.Length == 0 || (pack.FindArea(_viewId) is null && !_viewId.StartsWith(UnpinnedPrefix)))
        {
            _viewId = pack.Areas.Count > 0 ? pack.Areas[0].Id : "";
        }

        var area = pack.FindArea(_viewId);
        var linkInThisView = _linkAreaId.Length > 0 && _linkAreaId == _viewId;
        _insideView = interior is not null && linkInThisView && !_showOutside;
        Areas = new ObservableCollection<MapAreaItem>(AreaItems(pack));

        if (_insideView)
        {
            ShowInterior(pack, interior!, outside!, insideName);
        }
        else if (area is not null)
        {
            ShowArea(pack, area, linkInThisView && here is null);
        }
        else
        {
            ShowUnpinned(_viewId.EndsWith("mm", StringComparison.Ordinal) ? Game.Mm : Game.Oot);
        }

        LinkIsHere = linkInThisView && (here is not null || _insideView);
        var elsewhere = _linkAreaId.Length > 0 && !linkInThisView;
        var peeking = linkInThisView && here is null && !_insideView && insideName.Length > 0;
        Notice = elsewhere ? $"Link is in {(insideName.Length > 0 ? insideName : outside!.Title)} · show"
            : peeking ? $"Link is inside {insideName}{(interior is not null ? " · show" : "")}" : "";
        NoticeJumps = elsewhere || (peeking && interior is not null);
        RaiseAll();
    }

    private IEnumerable<MapAreaItem> AreaItems(EntranceMapPack pack)
    {
        foreach (var game in new[] { Game.Oot, Game.Mm })
        {
            var tag = GameInfo.ToSeedTag(game).ToUpperInvariant();
            yield return new MapAreaItem(true, "", GameInfo.Title(game), tag, "", false, false, false);
            foreach (var area in pack.Areas.Where(a => a.Game == game))
            {
                var views = _map!.PinsOf(area);
                var total = views.Count(v => v.Tracked);
                var found = views.Count(v => v.State == MapPinState.Found);
                yield return new MapAreaItem(false, area.Id, area.Title, tag, total > 0 ? $"{found}/{total}" : "–",
                                             total > 0 && found == total, area.Id == _linkAreaId, area.Id == _viewId);
            }

            var unpinned = _map!.Unpinned(game);
            if (unpinned.Count > 0)
            {
                var id = UnpinnedPrefix + GameInfo.ToSeedTag(game);
                var found = unpinned.Count(v => v.State == MapPinState.Found);
                yield return new MapAreaItem(false, id, "Dungeons and others", tag, $"{found}/{unpinned.Count}",
                                             found == unpinned.Count, false, id == _viewId);
            }
        }
    }

    private void ShowArea(EntranceMapPack pack, MapArea area, bool linkWentInside)
    {
        _views = _map!.PinsOf(area);
        Title = area.Title;
        GameTag = GameInfo.ToSeedTag(area.Game).ToUpperInvariant();
        SetPicture(pack.ImagePath(area.Image), area.Width, area.Height);

        var total = _views.Count(v => v.Tracked);
        var found = _views.Count(v => v.State == MapPinState.Found);
        CountText = total > 0 ? $"{found} of {total} entrances found here" : "No shuffled entrances here";
        CountComplete = total > 0 && found == total;

        var numbers = Numbers(area);
        var cameIndex = CameIndex(area, linkWentInside);
        var rows = new List<MapRowItem>();
        AddGroup(rows, "Not explored", MapPinState.Unexplored, area.Game, numbers, cameIndex, linkWentInside);
        AddGroup(rows, "Found", MapPinState.Found, area.Game, numbers, cameIndex, linkWentInside);
        if (_showFixed)
        {
            AddGroup(rows, "Fixed exits", MapPinState.Fixed, area.Game, numbers, cameIndex, linkWentInside);
        }

        Rows = new ObservableCollection<MapRowItem>(rows);
        RebuildPins();
    }

    private void AddGroup(
        List<MapRowItem> rows, string header, MapPinState state, Game game,
        IReadOnlyDictionary<int, int> numbers, int cameIndex, bool linkWentInside)
    {
        var ordered = _views.Where(v => v.State == state)
                            .OrderBy(v => numbers.GetValueOrDefault(v.PinIndex, int.MaxValue)).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        rows.Add(new MapRowItem(HeaderRow, header, "", "", "", "", false, false, "", -1, ""));
        foreach (var view in ordered)
        {
            var known = view.State != MapPinState.Unexplored;
            var otherGame = known && view.DestinationGame != game
                ? GameInfo.ToSeedTag(view.DestinationGame).ToUpperInvariant() : "";
            rows.Add(new MapRowItem(
                EntranceRow,
                view.Door,
                known ? $"→ {view.Destination}" : "not explored yet",
                otherGame,
                numbers.TryGetValue(view.PinIndex, out var number) ? number.ToString() : "",
                Style(view),
                view.LeadsToDungeon,
                view.Kind == MapPinKind.Grotto,
                view.PinIndex >= 0 && view.PinIndex == cameIndex ? linkWentInside ? "went in" : "came in here" : "",
                view.PinIndex,
                known && view.DestinationArea != _viewId ? view.DestinationArea : ""));
        }
    }

    private void ShowInterior(EntranceMapPack pack, MapInterior interior, MapArea outside, string name)
    {
        _views = [];
        Title = name;
        GameTag = GameInfo.ToSeedTag(outside.Game).ToUpperInvariant();
        SetPicture(pack.ImagePath(interior.Image), interior.Width, interior.Height);
        CountText = "Inside a grotto";
        CountComplete = false;
        var hole = outside.Pins.FirstOrDefault(p => p.Entrances.Contains(_position!.TakenKey));
        Rows =
        [
            new MapRowItem(HeaderRow, "Inside", "", "", "", "", false, false, "", -1, ""),
            new MapRowItem(WayOutRow, "Way out",
                           $"→ {outside.Title}{(hole is null ? "" : ", " + hole.Door)}", "", "↑", "exit",
                           false, false, "", WayOutPin, ""),
            new MapRowItem(ShowOutsideRow, $"Show {outside.Title} instead", "", "", "", "", false, false, "", -1,
                           ""),
        ];
        RebuildPins();
    }

    private void ShowUnpinned(Game game)
    {
        _views = _map!.Unpinned(game);
        Title = "Dungeons and others";
        GameTag = GameInfo.ToSeedTag(game).ToUpperInvariant();
        SetPicture("", 1, 1);
        var found = _views.Count(v => v.State == MapPinState.Found);
        CountText = $"{found} of {_views.Count} entrances found";
        CountComplete = _views.Count > 0 && found == _views.Count;

        var rows = new List<MapRowItem>();
        var numbers = new Dictionary<int, int>();
        AddGroup(rows, "Not explored", MapPinState.Unexplored, game, numbers, -1, false);
        AddGroup(rows, "Found", MapPinState.Found, game, numbers, -1, false);
        Rows = new ObservableCollection<MapRowItem>(rows);
        Pins = [];
    }

    private void RebuildPins()
    {
        if (_map is null || !HasPicture || _mapWidth < 1 || _mapHeight < 1)
        {
            Pins = [];
            Raise(nameof(Pins));
            return;
        }

        if (_insideView)
        {
            var interior = _map.Pack.InteriorOfEntrance(_position!.Game, _position.Entrance)!;
            var x = Math.Clamp(interior.ExitX * _mapWidth, 13, Math.Max(13, _mapWidth - 13));
            var y = Math.Clamp(interior.ExitY * _mapHeight, 13, Math.Max(13, _mapHeight - 13));
            Pins = [new MapPinItem(WayOutPin, x, y, x, y, false, "↑", "exit", false, false, false, "Way out")];
            Raise(nameof(Pins));
            return;
        }

        var area = _map.Pack.FindArea(_viewId);
        if (area is null)
        {
            Pins = [];
            Raise(nameof(Pins));
            return;
        }

        var shown = _views.Where(v => v.Tracked || _showFixed).ToList();
        var anchors = shown.Select(v => (area.Pins[v.PinIndex].X * _mapWidth, area.Pins[v.PinIndex].Y * _mapHeight,
                                         v.Tracked ? TrackedRadius : FixedRadius)).ToList();
        var placed = MapPinLayout.Spread(anchors, _mapWidth, _mapHeight);
        var numbers = Numbers(area);
        var linkInside = _position is not null && _map.Pack.AreaOfScene(_position.Game, _position.Scene) is null;
        var cameIndex = _viewId == _linkAreaId ? CameIndex(area, linkInside) : -1;
        var pins = new List<MapPinItem>(shown.Count);
        for (var i = 0; i < shown.Count; i++)
        {
            var view = shown[i];
            var moved = Math.Sqrt(Math.Pow(placed[i].X - anchors[i].Item1, 2) + Math.Pow(placed[i].Y - anchors[i].Item2, 2));
            pins.Add(new MapPinItem(
                view.PinIndex, placed[i].X, placed[i].Y, anchors[i].Item1, anchors[i].Item2, moved > anchors[i].Item3,
                numbers.TryGetValue(view.PinIndex, out var number) ? number.ToString() : "",
                Style(view), view.LeadsToDungeon, view.Kind == MapPinKind.Grotto, view.PinIndex == cameIndex,
                view.Door));
        }

        Pins = new ObservableCollection<MapPinItem>(pins);
        Raise(nameof(Pins));
    }

    private Dictionary<int, int> Numbers(MapArea area) =>
        _views.Where(v => v.Tracked)
              .OrderBy(v => area.Pins[v.PinIndex].Y)
              .ThenBy(v => area.Pins[v.PinIndex].X)
              .Select((view, order) => (view.PinIndex, Number: order + 1))
              .ToDictionary(p => p.PinIndex, p => p.Number);

    private int CameIndex(MapArea area, bool linkWentInside)
    {
        if (_position is null || area.Id != _linkAreaId)
        {
            return -1;
        }

        var key = linkWentInside ? _position.TakenKey : _position.ArrivalKey;
        if (key.Length == 0)
        {
            return -1;
        }

        for (var index = 0; index < area.Pins.Count; index++)
        {
            if (area.Pins[index].Entrances.Contains(key))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Style(MapEntranceView view) => view.State switch
    {
        MapPinState.Found => "found",
        MapPinState.Unexplored => "open",
        _ => view.Kind == MapPinKind.Grotto ? "hole" : "fixed",
    };

    private void SetPicture(string path, int width, int height)
    {
        HasPicture = path.Length > 0;
        ImageSource = path.Length > 0 ? new Uri(path).AbsoluteUri : "";
        ImageWidth = Math.Max(1, width);
        ImageHeight = Math.Max(1, height);
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Areas), nameof(Pins), nameof(Rows), nameof(Title), nameof(GameTag), nameof(LinkIsHere),
                     nameof(HasPicture), nameof(ShowingInterior), nameof(ImageSource), nameof(ImageWidth), nameof(ImageHeight),
                     nameof(CountText), nameof(CountComplete), nameof(Notice), nameof(NoticeJumps),
                 })
        {
            Raise(name);
        }
    }
}
