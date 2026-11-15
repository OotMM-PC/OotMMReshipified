namespace OotMM.Core;

public sealed class Seed
{
    public const string CurrentFormat = "ootmm.pc.seed.v2";
    public const string RequiredGeneratorVersion = "31.1";

    public sealed record CosmeticsInfo(
        bool GerudoTunic, bool MusicCategories, bool MusicNames, string MusicCompanionFile, bool DefaultHold = false);

    public sealed record WorldFlag(bool All, IReadOnlyList<string> Members);

    public sealed record SpecialCond(int Count, IReadOnlyDictionary<string, bool> Fields)
    {
        public bool Field(string name) => Fields.TryGetValue(name, out var value) && value;
    }

    public required string Format { get; init; }
    public required string GeneratorVersion { get; init; }
    public required string SeedId { get; init; }
    public string SeedName { get; init; } = "";
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
    public IReadOnlyList<uint> EntrancesSpawn { get; init; } = [];
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

    private static readonly (string Owl, string CheckId)[] PreActivatedOwlChecks =
    [
        ("clocktown", "MM_CLOCK_TOWN_OWL_STATUE"),
        ("milkroad", "MM_MILK_ROAD_OWL_STATUE"),
        ("swamp", "MM_SOUTHERN_SWAMP_OWL_STATUE"),
        ("woodfall", "MM_WOODFALL_OWL_STATUE"),
        ("mountain", "MM_MOUNTAIN_VILLAGE_OWL_STATUE"),
        ("snowhead", "MM_SNOWHEAD_OWL_STATUE"),
        ("greatbay", "MM_GREAT_BAY_COAST_OWL_STATUE"),
        ("zoracape", "MM_ZORA_CAPE_OWL_STATUE"),
        ("canyon", "MM_IKANA_CANYON_OWL_STATUE"),
        ("tower", "MM_STONE_TOWER_OWL_STATUE"),
    ];

    private Dictionary<string, ItemPlacement>? _placementIndex;
    private HashSet<string>? _startingChecks;
    private bool? _needsMasterQuest;

    public bool IsMultiworld => PlayerCount > 1 || GetStringSetting("mode", "single") != "single";

    public bool IsSpawnDestination(Game game, uint nativeId) =>
        IsWarpDestination(EntrancesSpawn, Game.Oot, game, nativeId);

    public bool IsSongDestination(Game game, uint nativeId) =>
        IsWarpDestination(EntrancesSong, Game.Oot, game, nativeId);

    public bool IsOwlDestination(Game game, uint nativeId) =>
        IsWarpDestination(EntrancesOwl, Game.Mm, game, nativeId);

    private static bool IsWarpDestination(IReadOnlyList<uint> destinations, Game sourceGame, Game game, uint nativeId) =>
        game is Game.Oot or Game.Mm && (nativeId & 0x80000000u) == 0
        && destinations.Contains(game != sourceGame ? nativeId | 0x80000000u : nativeId);

    public int PlayableCheckCount => Placements.Count(IsTrackedPlacement);

    public bool IsTrackedPlacement(ItemPlacement placement) =>
        !IsStartingCheck(placement.Check.Id) && !FixedChecks.IsFixed(this, placement);

    public bool IsStartingCheck(string checkId) => (_startingChecks ??= BuildStartingChecks()).Contains(checkId);

    private HashSet<string> BuildStartingChecks()
    {
        var checks = new HashSet<string>(StringComparer.Ordinal);
        if (GetBoolSetting("skipZelda"))
        {
            checks.Add("OOT_ZELDAS_LETTER");
            checks.Add("OOT_ZELDAS_SONG");
            if (GetStringSetting("shuffleWonderItemsOot", "none") is "all" or "overworld")
                checks.Add("OOT_CASTLE_COURTYARD_WONDER_ITEM");
        }

        if (GetStringSetting("gerudoFortress") == "open")
            checks.Add("OOT_GERUDO_MEMBER_CARD");

        if (WorldFlags.TryGetValue("mmPreActivatedOwls", out var owls))
        {
            foreach (var (owl, checkId) in PreActivatedOwlChecks)
            {
                if (owls.All || owls.Members.Contains(owl))
                    checks.Add(checkId);
            }
        }

        return checks;
    }

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

    public bool NeedsMasterQuest() =>
        _needsMasterQuest ??= Placements.Any(p =>
            p.Check.Game == Game.Oot && p.Check.Id.StartsWith("OOT_MQ_", StringComparison.Ordinal));

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
