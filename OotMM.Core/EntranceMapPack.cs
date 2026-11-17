using System.Text.Json;

namespace OotMM.Core;

public enum MapPinKind
{
    Exit,
    Grotto,
}

public sealed record MapPin(double X, double Y, MapPinKind Kind, string Door, IReadOnlyList<string> Entrances);

public sealed record MapArea(
    string Id, Game Game, string Title, string Image, int Width, int Height, IReadOnlyList<MapPin> Pins);

public sealed record MapInterior(string Id, string Image, int Width, int Height, double ExitX, double ExitY);

public sealed record MapEntrance(
    string Key, Game Game, uint NativeId, string Type, string From, string Name, string Area, string Interior,
    string Reverse);

/// <summary>The installed map pictures and the tables that tie their pins to entrance names.</summary>
public sealed class EntranceMapPack
{
    private const string FolderName = "maps";
    private const string IndexName = "index.json";

    private readonly Dictionary<string, MapArea> _areas = new(StringComparer.Ordinal);
    private readonly Dictionary<(Game, uint), MapEntrance> _byNativeId = [];
    private readonly Dictionary<(Game, int), string> _areaOfScene = [];
    private readonly Dictionary<(Game, uint), string> _interiorOfEntrance = [];
    private readonly Dictionary<(Game, uint), string> _places = [];

    private EntranceMapPack(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public IReadOnlyList<MapArea> Areas { get; private set; } = [];

    public IReadOnlyDictionary<string, MapInterior> Interiors { get; private set; } =
        new Dictionary<string, MapInterior>();

    public IReadOnlyDictionary<string, MapEntrance> Entrances { get; private set; } =
        new Dictionary<string, MapEntrance>();

    public MapArea? FindArea(string id) => _areas.GetValueOrDefault(id);

    public MapArea? AreaOfScene(Game game, int scene) =>
        _areaOfScene.TryGetValue((game, scene), out var id) ? FindArea(id) : null;

    public MapEntrance? FindEntrance(Game game, uint nativeId) => _byNativeId.GetValueOrDefault((game, nativeId));

    /// <summary>The grotto interior a loaded entrance shows, or null when it has no picture.</summary>
    public MapInterior? InteriorOfEntrance(Game game, uint loaded) =>
        _interiorOfEntrance.TryGetValue((game, WithoutSceneLayer(game, loaded)), out var id)
            ? Interiors.GetValueOrDefault(id) : null;

    /// <summary>A readable name for wherever a loaded entrance puts the player.</summary>
    public string PlaceName(Game game, uint loaded) =>
        FindEntrance(game, WithoutSceneLayer(game, loaded))?.Name
        ?? _places.GetValueOrDefault((game, game == Game.Mm ? loaded >> 9 : loaded), "");

    public string ImagePath(string image) => Path.Combine(Directory, image);

    /// <summary>Loads the pack shipped beside the launcher, or returns null when it is missing.</summary>
    public static EntranceMapPack? LoadInstalled() => Load(Path.Combine(AppContext.BaseDirectory, FolderName));

    /// <summary>Loads the pack in a folder, or returns null when the folder holds no usable pack.</summary>
    public static EntranceMapPack? Load(string directory)
    {
        try
        {
            var index = Path.Combine(directory, IndexName);
            if (!File.Exists(index))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(index));
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                return null;
            }

            var pack = new EntranceMapPack(directory);
            pack.ReadAreas(root.GetProperty("areas"));
            pack.ReadInteriors(root.GetProperty("interiors"));
            pack.ReadEntrances(root.GetProperty("entrances"));
            ReadGameTable(root.GetProperty("scenes"), (game, key, value) =>
                pack._areaOfScene[(game, (int)key)] = value);
            ReadGameTable(root.GetProperty("interiorEntrances"), (game, key, value) =>
                pack._interiorOfEntrance[(game, key)] = value);
            ReadGameTable(root.GetProperty("places"), (game, key, value) => pack._places[(game, key)] = value);
            return pack;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                                          or KeyNotFoundException or InvalidOperationException
                                          or FormatException)
        {
            return null;
        }
    }

    private void ReadAreas(JsonElement areas)
    {
        var list = new List<MapArea>();
        foreach (var area in areas.EnumerateArray())
        {
            var pins = new List<MapPin>();
            foreach (var pin in area.GetProperty("pins").EnumerateArray())
            {
                pins.Add(new MapPin(
                    pin.GetProperty("x").GetDouble(),
                    pin.GetProperty("y").GetDouble(),
                    pin.GetProperty("kind").GetString() == "grotto" ? MapPinKind.Grotto : MapPinKind.Exit,
                    pin.GetProperty("door").GetString() ?? "",
                    [.. pin.GetProperty("entrances").EnumerateArray().Select(e => e.GetString() ?? "")]));
            }

            var parsed = new MapArea(
                area.GetProperty("id").GetString() ?? "",
                ParseGame(area.GetProperty("game").GetString()),
                area.GetProperty("title").GetString() ?? "",
                area.GetProperty("image").GetString() ?? "",
                area.GetProperty("width").GetInt32(),
                area.GetProperty("height").GetInt32(),
                pins);
            list.Add(parsed);
            _areas[parsed.Id] = parsed;
        }

        Areas = list;
    }

    private void ReadInteriors(JsonElement interiors)
    {
        var map = new Dictionary<string, MapInterior>(StringComparer.Ordinal);
        foreach (var entry in interiors.EnumerateObject())
        {
            var exit = entry.Value.GetProperty("exit");
            map[entry.Name] = new MapInterior(
                entry.Name,
                entry.Value.GetProperty("image").GetString() ?? "",
                entry.Value.GetProperty("width").GetInt32(),
                entry.Value.GetProperty("height").GetInt32(),
                exit.GetProperty("x").GetDouble(),
                exit.GetProperty("y").GetDouble());
        }

        Interiors = map;
    }

    private void ReadEntrances(JsonElement entrances)
    {
        var map = new Dictionary<string, MapEntrance>(StringComparer.Ordinal);
        foreach (var entry in entrances.EnumerateObject())
        {
            var value = entry.Value;
            var parsed = new MapEntrance(
                entry.Name,
                ParseGame(value.GetProperty("game").GetString()),
                value.GetProperty("id").GetUInt32(),
                value.GetProperty("type").GetString() ?? "",
                value.GetProperty("from").GetString() ?? "",
                value.GetProperty("name").GetString() ?? "",
                value.GetProperty("area").GetString() ?? "",
                value.GetProperty("interior").GetString() ?? "",
                value.GetProperty("reverse").GetString() ?? "");
            map[entry.Name] = parsed;
            _byNativeId[(parsed.Game, parsed.NativeId)] = parsed;
        }

        Entrances = map;
    }

    private static void ReadGameTable(JsonElement table, Action<Game, uint, string> add)
    {
        foreach (var game in table.EnumerateObject())
        {
            foreach (var entry in game.Value.EnumerateObject())
            {
                add(ParseGame(game.Name), uint.Parse(entry.Name, System.Globalization.CultureInfo.InvariantCulture),
                    entry.Value.GetString() ?? "");
            }
        }
    }

    private static Game ParseGame(string? tag) => tag == "mm" ? Game.Mm : Game.Oot;

    private static uint WithoutSceneLayer(Game game, uint loaded) => game == Game.Mm ? loaded & 0xFFF0u : loaded;
}
