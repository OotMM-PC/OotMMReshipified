namespace OotMM.Core;

public enum MapPinState
{
    Fixed,
    Unexplored,
    Found,
}

/// <summary>One entrance as the map shows it: its state and, once known, where it leads.</summary>
public sealed record MapEntranceView(
    int PinIndex,
    string Key,
    string Door,
    MapPinKind Kind,
    MapPinState State,
    string Destination,
    Game DestinationGame,
    string DestinationArea,
    string DestinationInterior,
    bool LeadsToDungeon)
{
    public bool Tracked => State != MapPinState.Fixed;
}

/// <summary>Where the player is: the loaded scene, the entrance left through and the one arrived by.</summary>
public sealed record MapPosition(Game Game, int Scene, uint Entrance, string TakenKey, string ArrivalKey);

/// <summary>What the map shows for one seed: which entrances are shuffled, which were found and where they lead.</summary>
public sealed class EntranceMap
{
    private readonly EntranceMapPack _pack;
    private readonly Dictionary<string, EntranceMapping> _shuffled = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pinned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _discovered = new(StringComparer.Ordinal);
    private readonly bool _coupled;
    private string _leftThrough = "";

    public EntranceMap(Seed seed, EntranceMapPack pack)
    {
        _pack = pack;
        _coupled = !seed.GetBoolSetting("erDecoupled");
        foreach (var mapping in seed.Entrances)
        {
            if (mapping.From.Length > 0 && mapping.From != mapping.To && pack.Entrances.ContainsKey(mapping.From))
            {
                _shuffled[mapping.From] = mapping;
            }
        }

        foreach (var key in pack.Areas.SelectMany(area => area.Pins).SelectMany(pin => pin.Entrances))
        {
            _pinned.Add(key);
        }
    }

    public EntranceMapPack Pack => _pack;

    public IReadOnlyCollection<string> Discovered => _discovered;

    public MapPosition? Position { get; private set; }

    /// <summary>Records an entrance the player went through; false when it adds nothing new.</summary>
    public bool Discover(string key)
    {
        if (!_shuffled.TryGetValue(key, out var mapping) || !_discovered.Add(key))
        {
            return false;
        }

        if (_coupled
            && _pack.Entrances.TryGetValue(mapping.To, out var target)
            && target.Reverse.Length > 0
            && _shuffled.ContainsKey(target.Reverse))
        {
            _discovered.Add(target.Reverse);
        }

        return true;
    }

    private MapEntrance? TargetOf(string key) =>
        _pack.Entrances.GetValueOrDefault(_shuffled.TryGetValue(key, out var mapping) ? mapping.To : key);

    /// <summary>Notes the entrance the player is leaving through; empty when it is not one the pack knows.</summary>
    public void Leave(string key) => _leftThrough = key;

    /// <summary>
    /// Moves the player to the scene the game reports as loaded. The entrance noted by Leave is used when a report
    /// names a place it leads to: a report can still name the scene being left while the next one loads.
    /// </summary>
    public void Arrive(Game game, int scene, uint entrance)
    {
        var previous = Position;
        var sameScene = previous is not null && previous.Game == game && previous.Scene == scene;
        var area = _pack.AreaOfScene(game, scene);
        if (_leftThrough.Length > 0 && LeadsInto(_leftThrough, game, area))
        {
            Position = new MapPosition(game, scene, entrance, _leftThrough, TargetOf(_leftThrough)?.Reverse ?? "");
        }
        else if (sameScene)
        {
            Position = previous! with { Entrance = entrance };
        }
        else
        {
            var cameOutTheWayIn = previous is { TakenKey.Length: > 0 }
                                  && _pack.AreaOfScene(previous.Game, previous.Scene) is null
                                  && area is not null
                                  && area.Pins.Any(pin => pin.Entrances.Contains(previous.TakenKey));
            Position = new MapPosition(game, scene, entrance, "", cameOutTheWayIn ? previous!.TakenKey : "");
        }

        if (!sameScene)
        {
            _leftThrough = "";
        }
    }

    /// <summary>Forgets everything found and where the player is.</summary>
    public void Reset()
    {
        _discovered.Clear();
        _leftThrough = "";
        Position = null;
    }

    /// <summary>Replaces what was found with the given entrances; ones the seed does not shuffle are ignored.</summary>
    public void Restore(IEnumerable<string> keys)
    {
        _discovered.Clear();
        foreach (var key in keys)
        {
            Discover(key);
        }
    }

    /// <summary>The name of the place an entrance leads to in this seed, or empty when unknown.</summary>
    public string DestinationOf(string key) => TargetOf(key)?.Name ?? "";

    public IReadOnlyList<MapEntranceView> PinsOf(MapArea area)
    {
        var views = new List<MapEntranceView>(area.Pins.Count);
        for (var index = 0; index < area.Pins.Count; index++)
        {
            var pin = area.Pins[index];
            var key = pin.Entrances.FirstOrDefault(_shuffled.ContainsKey) ?? pin.Entrances.FirstOrDefault() ?? "";
            var found = pin.Entrances.Any(_discovered.Contains);
            views.Add(View(index, key, pin.Door, pin.Kind, found, area.Game));
        }

        return views;
    }

    /// <summary>Shuffled entrances of a game that no map picture has a pin for.</summary>
    public IReadOnlyList<MapEntranceView> Unpinned(Game game) =>
        [.. _shuffled.Values
            .Where(m => m.FromGame == game && !_pinned.Contains(m.From)
                        && !_pack.Entrances[m.From].Type.StartsWith("spawn", StringComparison.Ordinal))
            .OrderBy(m => _pack.Entrances[m.From].From, StringComparer.Ordinal)
            .ThenBy(m => m.From, StringComparer.Ordinal)
            .Select(m =>
            {
                var entrance = _pack.Entrances[m.From];
                return View(-1, m.From, $"{entrance.From}: {entrance.Name} entrance", MapPinKind.Exit,
                            _discovered.Contains(m.From), game);
            })];

    private MapEntranceView View(int index, string key, string door, MapPinKind kind, bool found, Game game)
    {
        var state = !_shuffled.ContainsKey(key) ? MapPinState.Fixed
            : found ? MapPinState.Found : MapPinState.Unexplored;
        var target = TargetOf(key);
        var fallback = door.EndsWith(" entrance", StringComparison.Ordinal) ? door[..^9] : door;
        return new MapEntranceView(
            index,
            key,
            door,
            kind,
            state,
            state == MapPinState.Unexplored ? "" : target?.Name ?? fallback,
            target?.Game ?? game,
            state == MapPinState.Unexplored ? "" : target?.Area ?? "",
            state == MapPinState.Unexplored ? "" : target?.Interior ?? "",
            state == MapPinState.Found && target is not null && IsDungeon(target.Type));
    }

    private bool LeadsInto(string key, Game game, MapArea? area)
    {
        var target = TargetOf(key);
        return target is not null && target.Game == game && target.Area == (area?.Id ?? "");
    }

    private static bool IsDungeon(string type) =>
        type == "boss"
        || (type.StartsWith("dungeon", StringComparison.Ordinal) && !type.EndsWith("-exit", StringComparison.Ordinal));
}
