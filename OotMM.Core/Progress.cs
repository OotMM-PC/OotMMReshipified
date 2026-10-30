namespace OotMM.Core;

public enum SoulSub
{
    Enemy,
    Boss,
    Npc,
    Animal,
    Misc,
}

public sealed record DungeonProgress(
    string Name, Game Game,
    int KeysHave, int KeysTotal,
    bool HasKeyRing, bool KeyRing, bool SkeletonOwned,
    bool HasMap, bool Map,
    bool HasCompass, bool Compass,
    bool HasBossKey, bool BossKey,
    bool HasFairies, int FairiesHave, int FairiesTotal);

public sealed record ItemProgress(string Id, string Name, int Have, int Total)
{
    public bool Owned => Have > 0;
}

public sealed record ChecksSummary(int Have, int Total, int OotHave, int OotTotal, int MmHave, int MmTotal);

public sealed record GoalProgress(string Name, int Owned, int Threshold)
{
    public bool Active => Threshold > 0;

    public bool Satisfied => Owned >= Threshold;
}

public sealed class ProgressReport
{
    private static readonly (string Name, Game Game, string Token)[] Dungeons =
    [
        ("Deku Tree", Game.Oot, "DT"), ("Dodongo's Cavern", Game.Oot, "DC"),
        ("Jabu Jabu", Game.Oot, "JJ"), ("Forest Temple", Game.Oot, "FOREST"),
        ("Fire Temple", Game.Oot, "FIRE"), ("Water Temple", Game.Oot, "WATER"),
        ("Spirit Temple", Game.Oot, "SPIRIT"), ("Shadow Temple", Game.Oot, "SHADOW"),
        ("Bottom of the Well", Game.Oot, "BOTW"), ("Ice Cavern", Game.Oot, "IC"),
        ("Gerudo Fortress", Game.Oot, "GF"), ("Gerudo Training", Game.Oot, "GTG"),
        ("Chest Game", Game.Oot, "TCG"), ("Ganon's Castle", Game.Oot, "GANON"),
        ("Woodfall Temple", Game.Mm, "WF"), ("Snowhead Temple", Game.Mm, "SH"),
        ("Great Bay Temple", Game.Mm, "GB"), ("Stone Tower Temple", Game.Mm, "ST"),
        ("Clock Town", Game.Mm, "TOWN"),
    ];

    private static readonly (string Field, string[] Ids, bool Unique)[] GoalFields =
    [
        ("stones", ["OOT_STONE_EMERALD", "OOT_STONE_RUBY", "OOT_STONE_SAPPHIRE"], false),
        ("medallions", ["OOT_MEDALLION_FOREST", "OOT_MEDALLION_FIRE", "OOT_MEDALLION_WATER",
                        "OOT_MEDALLION_SPIRIT", "OOT_MEDALLION_SHADOW", "OOT_MEDALLION_LIGHT"], false),
        ("remains", ["MM_REMAINS_ODOLWA", "MM_REMAINS_GOHT", "MM_REMAINS_GYORG", "MM_REMAINS_TWINMOLD"], false),
        ("skullsGold", ["OOT_GS_TOKEN"], false),
        ("skullsSwamp", ["MM_GS_TOKEN_SWAMP"], false),
        ("skullsOcean", ["MM_GS_TOKEN_OCEAN"], false),
        ("fairiesWF", ["MM_STRAY_FAIRY_WF"], false),
        ("fairiesSH", ["MM_STRAY_FAIRY_SH"], false),
        ("fairiesGB", ["MM_STRAY_FAIRY_GB"], false),
        ("fairiesST", ["MM_STRAY_FAIRY_ST"], false),
        ("fairyTown", ["MM_STRAY_FAIRY_TOWN"], false),
        ("masksRegular", ["MM_MASK_POSTMAN", "MM_MASK_ALL_NIGHT", "MM_MASK_BLAST", "MM_MASK_STONE",
                          "MM_MASK_GREAT_FAIRY", "MM_MASK_KEATON", "MM_MASK_BREMEN", "MM_MASK_BUNNY",
                          "MM_MASK_DON_GERO", "MM_MASK_SCENTS", "MM_MASK_ROMANI", "MM_MASK_TROUPE_LEADER",
                          "MM_MASK_KAFEI", "MM_MASK_COUPLE", "MM_MASK_TRUTH", "MM_MASK_KAMARO",
                          "MM_MASK_GIBDO", "MM_MASK_GARO", "MM_MASK_CAPTAIN", "MM_MASK_GIANT",
                          "MM_MASK_GERUDO", "MM_MASK_SKULL", "MM_MASK_SPOOKY", "SHARED_MASK_TRUTH",
                          "SHARED_MASK_BUNNY", "SHARED_MASK_KEATON", "SHARED_MASK_BLAST", "SHARED_MASK_STONE",
                          "SHARED_MASK_KAMARO", "SHARED_MASK_GERUDO", "SHARED_MASK_SKULL", "SHARED_MASK_SPOOKY"], true),
        ("masksTransform", ["MM_MASK_DEKU", "MM_MASK_GORON", "MM_MASK_ZORA", "MM_MASK_FIERCE_DEITY",
                            "SHARED_MASK_GORON", "SHARED_MASK_ZORA"], true),
        ("masksOot", ["OOT_MASK_KEATON", "OOT_MASK_SKULL", "OOT_MASK_SPOOKY", "OOT_MASK_BUNNY",
                      "OOT_MASK_GORON", "OOT_MASK_ZORA", "OOT_MASK_GERUDO", "OOT_MASK_TRUTH",
                      "OOT_MASK_BLAST", "OOT_MASK_STONE", "OOT_MASK_KAMARO", "SHARED_MASK_KEATON",
                      "SHARED_MASK_BUNNY", "SHARED_MASK_TRUTH", "SHARED_MASK_GORON", "SHARED_MASK_ZORA",
                      "SHARED_MASK_BLAST", "SHARED_MASK_STONE", "SHARED_MASK_KAMARO", "SHARED_MASK_SKULL",
                      "SHARED_MASK_SPOOKY", "SHARED_MASK_GERUDO"], true),
        ("triforce", ["SHARED_TRIFORCE", "SHARED_TRIFORCE_POWER", "SHARED_TRIFORCE_COURAGE",
                      "SHARED_TRIFORCE_WISDOM", "OOT_TRIFORCE", "MM_TRIFORCE"], false),
        ("coinsRed", ["OOT_COIN_RED", "MM_COIN_RED"], false),
        ("coinsGreen", ["OOT_COIN_GREEN", "MM_COIN_GREEN"], false),
        ("coinsBlue", ["OOT_COIN_BLUE", "MM_COIN_BLUE"], false),
        ("coinsYellow", ["OOT_COIN_YELLOW", "MM_COIN_YELLOW"], false),
    ];

    private static readonly string[] GoalNames = ["BRIDGE", "MOON", "LACS", "GANON_BK", "MAJORA"];

    private readonly Dictionary<string, int> _have;
    private readonly Dictionary<string, int> _pool = [];
    private readonly Dictionary<string, string> _names = [];

    public ChecksSummary Checks { get; }
    public IReadOnlyList<DungeonProgress> DungeonRows { get; }
    public IReadOnlyList<ItemProgress> Songs(Game game) => game == Game.Oot ? _songsOot : _songsMm;
    public IReadOnlyList<ItemProgress> KeyItems(Game game) => game == Game.Oot ? _keyOot : _keyMm;
    public IReadOnlyList<ItemProgress> RustyKeys(Game game) => game == Game.Oot ? _rustyOot : _rustyMm;
    public IReadOnlyList<ItemProgress> Souls(Game game, SoulSub sub) =>
        (game == Game.Oot ? _soulsOot : _soulsMm)[(int)sub];
    public IReadOnlyList<GoalProgress> Goals { get; }
    public int ItemsOwned { get; }

    private readonly List<ItemProgress> _songsOot = [], _songsMm = [];
    private readonly List<ItemProgress> _keyOot = [], _keyMm = [];
    private readonly List<ItemProgress> _rustyOot = [], _rustyMm = [];
    private readonly List<ItemProgress>[] _soulsOot;
    private readonly List<ItemProgress>[] _soulsMm;

    public static ProgressReport Build(
        Seed seed,
        IReadOnlyList<(ItemPlacement Placement, bool Collected)> checks,
        Dictionary<string, int> itemCounts,
        IReadOnlyList<(ItemPlacement Placement, bool Collected)>? foreignChecks = null) =>
        new(seed, checks, itemCounts, foreignChecks ?? []);

    private ProgressReport(
        Seed seed,
        IReadOnlyList<(ItemPlacement Placement, bool Collected)> checks,
        Dictionary<string, int> itemCounts,
        IReadOnlyList<(ItemPlacement Placement, bool Collected)> foreignChecks)
    {
        _have = itemCounts;
        _soulsOot = [.. Enumerable.Range(0, 5).Select(_ => new List<ItemProgress>())];
        _soulsMm = [.. Enumerable.Range(0, 5).Select(_ => new List<ItemProgress>())];

        var majorIds = new HashSet<string>();
        foreach (var (placement, _) in checks.Concat(foreignChecks))
        {
            if (!placement.IsFor(seed.PlayerId) || placement.Item.Id.Length == 0)
            {
                continue;
            }

            _pool[placement.Item.Id] = _pool.GetValueOrDefault(placement.Item.Id) + 1;
            if (placement.Item.Name.Length > 0)
            {
                _names.TryAdd(placement.Item.Id, placement.Item.Name);
            }

            if (placement.Major)
            {
                majorIds.Add(placement.Item.Id);
            }
        }

        ItemsOwned = _have.Values.Sum();

        int ootHave = 0, ootTotal = 0, mmHave = 0, mmTotal = 0;
        foreach (var (placement, collected) in checks)
        {
            if (placement.Check.Game == Game.Oot)
            {
                ootTotal++;
                ootHave += collected ? 1 : 0;
            }
            else
            {
                mmTotal++;
                mmHave += collected ? 1 : 0;
            }
        }

        Checks = new ChecksSummary(ootHave + mmHave, ootTotal + mmTotal, ootHave, ootTotal, mmHave, mmTotal);

        var dungeons = new List<DungeonProgress>();
        foreach (var (name, game, token) in Dungeons)
        {
            var oot = game == Game.Oot;
            var pre = oot ? "OOT_" : "MM_";
            var smallKeyId = $"{pre}SMALL_KEY_{token}";
            var ringId = $"{pre}KEY_RING_{token}";
            var bossId = $"{pre}BOSS_KEY_{token}";
            var mapId = $"{pre}MAP_{token}";
            var compassId = oot ? $"OOT_COMPASS_{token}" : "";
            var fairyId = oot ? "" : $"MM_STRAY_FAIRY_{token}";

            var row = new DungeonProgress(
                name, game,
                KeysHave: Have(smallKeyId), KeysTotal: Pool(smallKeyId),
                HasKeyRing: Pool(ringId) > 0, KeyRing: Have(ringId) > 0,
                SkeletonOwned: Have(oot ? "OOT_SKELETON_KEY" : "MM_SKELETON_KEY") > 0,
                HasMap: Pool(mapId) > 0, Map: Have(mapId) > 0,
                HasCompass: oot && Pool(compassId) > 0, Compass: oot && Have(compassId) > 0,
                HasBossKey: Pool(bossId) > 0, BossKey: Have(bossId) > 0,
                HasFairies: !oot && Pool(fairyId) > 0,
                FairiesHave: oot ? 0 : Have(fairyId), FairiesTotal: oot ? 0 : Pool(fairyId));

            if (row.KeysTotal > 0 || row.HasKeyRing || row.HasBossKey
                || row.HasMap || row.HasCompass || row.HasFairies)
            {
                dungeons.Add(row);
            }
        }

        DungeonRows = dungeons;

        foreach (var (id, total) in _pool)
        {
            if (ClassifySoul(id, out var soulGame, out var sub))
            {
                (soulGame == Game.Oot ? _soulsOot : _soulsMm)[(int)sub].Add(MakeItem(id, total));
                continue;
            }

            var isOotSong = id.StartsWith("OOT_SONG_", StringComparison.Ordinal);
            var isMmSong = id.StartsWith("MM_SONG_", StringComparison.Ordinal);
            var isSharedSong = id.StartsWith("SHARED_SONG_", StringComparison.Ordinal);
            if ((isOotSong || isMmSong || isSharedSong)
                && !id.Contains("_SONG_NOTE_", StringComparison.Ordinal))
            {
                var item = MakeItem(id, total);
                if (isSharedSong)
                {
                    _songsOot.Add(item);
                    _songsMm.Add(item);
                }
                else
                {
                    (isOotSong ? _songsOot : _songsMm).Add(item);
                }
            }

            if (id.StartsWith("OOT_RUSTY_KEY_", StringComparison.Ordinal))
            {
                _rustyOot.Add(MakeItem(id, total));
            }
            else if (id.StartsWith("MM_RUSTY_KEY_", StringComparison.Ordinal))
            {
                _rustyMm.Add(MakeItem(id, total));
            }

            if (majorIds.Contains(id))
            {
                var item = MakeItem(id, total);
                if (id.StartsWith("SHARED_", StringComparison.Ordinal))
                {
                    _keyOot.Add(item);
                    _keyMm.Add(item);
                }
                else if (id.StartsWith("OOT_", StringComparison.Ordinal))
                {
                    _keyOot.Add(item);
                }
                else
                {
                    _keyMm.Add(item);
                }
            }
        }

        var goals = new List<GoalProgress>();
        foreach (var goalName in GoalNames)
        {
            if (!seed.SpecialConds.TryGetValue(goalName, out var cond))
            {
                continue;
            }

            var owned = 0;
            if (cond.Count > 0)
            {
                var uniqueOwned = new HashSet<string>();
                foreach (var (field, ids, unique) in GoalFields)
                {
                    if (!cond.Field(field))
                    {
                        continue;
                    }

                    foreach (var id in ids)
                    {
                        var count = Have(id);
                        if (unique)
                        {
                            if (count > 0 && uniqueOwned.Add(id))
                            {
                                owned++;
                            }
                        }
                        else
                        {
                            owned += count;
                        }
                    }
                }
            }

            goals.Add(new GoalProgress(goalName, owned, cond.Count));
        }

        Goals = goals;
    }

    private int Have(string id) => _have.GetValueOrDefault(id);

    private int Pool(string id) => _pool.GetValueOrDefault(id);

    private ItemProgress MakeItem(string id, int total) =>
        new(id, _names.TryGetValue(id, out var name) ? name : id.Replace('_', ' '), Have(id), total);

    private static bool ClassifySoul(string id, out Game game, out SoulSub sub)
    {
        string[] subs = ["SOUL_ENEMY_", "SOUL_BOSS_", "SOUL_NPC_", "SOUL_ANIMAL_", "SOUL_MISC_"];
        foreach (var (prefix, candidate) in new[] { ("OOT_", Game.Oot), ("MM_", Game.Mm) })
        {
            for (var s = 0; s < subs.Length; s++)
            {
                if (id.StartsWith(prefix + subs[s], StringComparison.Ordinal))
                {
                    game = candidate;
                    sub = (SoulSub)s;
                    return true;
                }
            }
        }

        game = Game.Oot;
        sub = SoulSub.Enemy;
        return false;
    }
}
