namespace OotMM.Core;

/// <summary>
/// A loaded .ootmm.json seed manifest. Immutable after loading; construct only
/// through SeedLoader.
/// </summary>
public sealed class Seed
{
    public const string CurrentFormat = "ootmm.pc.seed.v2";
    public const string RequiredGeneratorVersion = "31.1";

    public sealed record CosmeticsInfo(
        bool GerudoTunic, bool MusicCategories, bool MusicNames, string MusicCompanionFile);

    public sealed record WorldFlag(bool All, IReadOnlyList<string> Members);

    public sealed record SpecialCond(int Count, IReadOnlyDictionary<string, bool> Fields)
    {
        public bool Field(string name) => Fields.TryGetValue(name, out var value) && value;
    }

    public required string Format { get; init; }
    public required string GeneratorVersion { get; init; }
    public required string SeedId { get; init; }
    public string SettingsHash { get; init; } = "";
    public ushort PlayerId { get; init; } = 1;
    public ushort PlayerCount { get; init; } = 1;
    public string TeamId { get; init; } = "default";
    public ulong SessionEpoch { get; init; } = 1;
    public Game? StartingGame { get; init; }
    public CosmeticsInfo Cosmetics { get; init; } = new(false, true, true, "");

    public IReadOnlyList<ItemPlacement> Placements { get; init; } = [];
    public IReadOnlyList<EntranceMapping> Entrances { get; init; } = [];
    public IReadOnlyList<StartingItem> StartingItems { get; init; } = [];

    public IReadOnlyList<int> SongEventsOot { get; init; } = [];
    public IReadOnlyList<int> SongEventsMm { get; init; } = [];
    public IReadOnlyList<int> Prices { get; init; } = [];
    public IReadOnlyList<uint> EntrancesSong { get; init; } = [];
    public IReadOnlyList<uint> EntrancesOwl { get; init; } = [];
    public IReadOnlyList<uint> DungeonWarps { get; init; } = [];
    public IReadOnlyList<int> BossIds { get; init; } = [];
    public IReadOnlyList<string> PreCompletedDungeons { get; init; } = [];

    public IReadOnlyDictionary<string, bool> BoolSettings { get; init; } =
        new Dictionary<string, bool>();
    public IReadOnlyDictionary<string, string> StringSettings { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, double> NumberSettings { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, WorldFlag> WorldFlags { get; init; } =
        new Dictionary<string, WorldFlag>();
    public IReadOnlyDictionary<string, SpecialCond> SpecialConds { get; init; } =
        new Dictionary<string, SpecialCond>();

    private Dictionary<string, ItemPlacement>? _placementIndex;
    private bool? _needsMasterQuest;

    public bool IsMultiworld => PlayerCount > 1;

    public bool GetBoolSetting(string name, bool fallback = false) =>
        BoolSettings.TryGetValue(name, out var value) ? value : fallback;

    public string GetStringSetting(string name, string fallback = "") =>
        StringSettings.TryGetValue(name, out var value) ? value : fallback;

    public int GetIntSetting(string name, int fallback = 0) =>
        NumberSettings.TryGetValue(name, out var value) ? (int)value : fallback;

    public Game ResolveStartingGame()
    {
        if (StartingGame is { } explicitGame)
        {
            return explicitGame;
        }

        if (StartingItems.Count > 0 && StartingItems.All(s => s.Item.Game == Game.Mm))
        {
            return Game.Mm;
        }

        return Game.Oot;
    }

    /// <summary>True when any placement targets a Master Quest dungeon layout,
    /// which only exists in oot-mq.o2r (so setup also needs an MQ ROM).</summary>
    public bool NeedsMasterQuest() =>
        _needsMasterQuest ??= Placements.Any(p =>
            p.Check.Game == Game.Oot && p.Check.Id.StartsWith("OOT_MQ_", StringComparison.Ordinal));

    /// <summary>Finds a placement by canonical id, display name, or alias.</summary>
    public ItemPlacement? FindPlacement(Game game, string checkId)
    {
        _placementIndex ??= BuildPlacementIndex();
        return _placementIndex.GetValueOrDefault(IndexKey(game, checkId));
    }

    private Dictionary<string, ItemPlacement> BuildPlacementIndex()
    {
        var index = new Dictionary<string, ItemPlacement>();
        foreach (var placement in Placements)
        {
            // First writer wins, matching the C++ index behavior.
            index.TryAdd(IndexKey(placement.Check.Game, placement.Check.Id), placement);
            if (placement.Check.Name.Length > 0)
            {
                index.TryAdd(IndexKey(placement.Check.Game, placement.Check.Name), placement);
            }

            foreach (var alias in placement.Check.Aliases)
            {
                index.TryAdd(IndexKey(placement.Check.Game, alias), placement);
            }
        }

        return index;
    }

    private static string IndexKey(Game game, string id) => $"{GameInfo.ToSeedTag(game)}|{id}";
}
