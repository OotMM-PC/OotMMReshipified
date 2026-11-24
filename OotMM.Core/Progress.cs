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
    int ChecksHave, int ChecksTotal,
    int KeysHave, int KeysTotal,
    bool HasKeyRing, bool KeyRing, bool SkeletonOwned,
    bool HasMap, bool Map,
    bool HasCompass, bool Compass,
    bool HasBossKey, bool BossKey,
    bool HasFairies, int FairiesHave, int FairiesTotal,
    int SilverHave, int SilverTotal)
{
    public bool HasItems => KeysTotal > 0 || HasKeyRing || HasBossKey || HasMap || HasCompass || HasFairies
                            || SilverTotal > 0;
}

public sealed record ItemProgress(string Id, string Name, int Have, int Total)
{
    public bool Owned => Have > 0;

    public bool Complete => Have >= Total;
}

public sealed record ItemGroupProgress(ItemGroup Group, IReadOnlyList<ItemProgress> Items);

public sealed record ChecksSummary(int Have, int Total, int OotHave, int OotTotal, int MmHave, int MmTotal);

public sealed record ForeignSummary(int Have, int Total);

public sealed record GoalPart(string Name, int Have, int Total);

public sealed record GoalProgress(
    string Name, string Title, int Owned, int Threshold, IReadOnlyList<GoalPart> Parts, IReadOnlyList<GoalPart> Needs)
{
    public bool Satisfied => Owned >= Threshold && Needs.All(n => n.Have >= n.Total);
}

/// <summary>A major item the player received: what it is, which check held it and which player found it.</summary>
public sealed record FoundItem(string ItemId, string ItemName, ItemGroup? Group, string CheckName, ushort Finder);

public sealed class ProgressReport
{
    private sealed record DungeonDef(string Name, Game Game, string Token, uint[] Scenes, string CheckPrefix = "");

    private static readonly DungeonDef[] Dungeons =
    [
        new("Deku Tree", Game.Oot, "DT", [0x00, 0x11], "OOT_DEKU_TREE_"),
        new("Dodongo's Cavern", Game.Oot, "DC", [0x01, 0x12], "OOT_DODONGO_CAVERN_"),
        new("Jabu-Jabu", Game.Oot, "JJ", [0x02, 0x13], "OOT_JABU_JABU_"),
        new("Forest Temple", Game.Oot, "FOREST", [0x03, 0x14], "OOT_FOREST_TEMPLE_"),
        new("Fire Temple", Game.Oot, "FIRE", [0x04, 0x15], "OOT_FIRE_TEMPLE_"),
        new("Water Temple", Game.Oot, "WATER", [0x05, 0x16], "OOT_WATER_TEMPLE_"),
        new("Spirit Temple", Game.Oot, "SPIRIT", [0x06, 0x17], "OOT_SPIRIT_TEMPLE_"),
        new("Shadow Temple", Game.Oot, "SHADOW", [0x07, 0x18], "OOT_SHADOW_TEMPLE_"),
        new("Bottom of the Well", Game.Oot, "BOTW", [0x08], "OOT_BOTTOM_OF_THE_WELL_"),
        new("Ice Cavern", Game.Oot, "IC", [0x09], "OOT_ICE_CAVERN_"),
        new("Thieves' Hideout", Game.Oot, "GF", [0x0C]),
        new("Gerudo Training Ground", Game.Oot, "GTG", [0x0B], "OOT_GERUDO_TRAINING_"),
        new("Treasure Chest Game", Game.Oot, "TCG", [0x10], "OOT_TREASURE_CHEST_GAME_"),
        new("Ganon's Castle", Game.Oot, "GANON", [0x0A, 0x0D]),
        new("Woodfall Temple", Game.Mm, "WF", [0x1B, 0x1F], "MM_WOODFALL_TEMPLE_"),
        new("Snowhead Temple", Game.Mm, "SH", [0x21, 0x44], "MM_SNOWHEAD_TEMPLE_"),
        new("Great Bay Temple", Game.Mm, "GB", [0x49, 0x5F], "MM_GREAT_BAY_TEMPLE_"),
        new("Stone Tower Temple", Game.Mm, "ST", [0x16, 0x18, 0x36], "MM_STONE_TOWER_TEMPLE_"),
    ];

    private static readonly string[] Medallions =
    [
        "OOT_MEDALLION_FOREST", "OOT_MEDALLION_FIRE", "OOT_MEDALLION_WATER",
        "OOT_MEDALLION_SPIRIT", "OOT_MEDALLION_SHADOW", "OOT_MEDALLION_LIGHT",
    ];

    private static readonly string[] Remains =
        ["MM_REMAINS_ODOLWA", "MM_REMAINS_GOHT", "MM_REMAINS_GYORG", "MM_REMAINS_TWINMOLD"];

    private static readonly (string Field, string Name, string[] Ids, bool Unique)[] GoalFields =
    [
        ("stones", "Spiritual Stones", ["OOT_STONE_EMERALD", "OOT_STONE_RUBY", "OOT_STONE_SAPPHIRE"], false),
        ("medallions", "Medallions", Medallions, false),
        ("remains", "Boss Remains", Remains, false),
        ("skullsGold", "Gold Skulltula tokens", ["OOT_GS_TOKEN"], false),
        ("skullsSwamp", "Swamp Skulltula tokens", ["MM_GS_TOKEN_SWAMP"], false),
        ("skullsOcean", "Ocean Skulltula tokens", ["MM_GS_TOKEN_OCEAN"], false),
        ("fairiesWF", "Woodfall fairies", ["MM_STRAY_FAIRY_WF"], false),
        ("fairiesSH", "Snowhead fairies", ["MM_STRAY_FAIRY_SH"], false),
        ("fairiesGB", "Great Bay fairies", ["MM_STRAY_FAIRY_GB"], false),
        ("fairiesST", "Stone Tower fairies", ["MM_STRAY_FAIRY_ST"], false),
        ("fairyTown", "Clock Town fairy", ["MM_STRAY_FAIRY_TOWN"], false),
        ("masksRegular", "Regular masks",
         ["MM_MASK_POSTMAN", "MM_MASK_ALL_NIGHT", "MM_MASK_BLAST", "MM_MASK_STONE",
          "MM_MASK_GREAT_FAIRY", "MM_MASK_KEATON", "MM_MASK_BREMEN", "MM_MASK_BUNNY",
          "MM_MASK_DON_GERO", "MM_MASK_SCENTS", "MM_MASK_ROMANI", "MM_MASK_TROUPE_LEADER",
          "MM_MASK_KAFEI", "MM_MASK_COUPLE", "MM_MASK_TRUTH", "MM_MASK_KAMARO",
          "MM_MASK_GIBDO", "MM_MASK_GARO", "MM_MASK_CAPTAIN", "MM_MASK_GIANT",
          "MM_MASK_GERUDO", "MM_MASK_SKULL", "MM_MASK_SPOOKY", "SHARED_MASK_TRUTH",
          "SHARED_MASK_BUNNY", "SHARED_MASK_KEATON", "SHARED_MASK_BLAST", "SHARED_MASK_STONE",
          "SHARED_MASK_KAMARO", "SHARED_MASK_GERUDO", "SHARED_MASK_SKULL", "SHARED_MASK_SPOOKY"], true),
        ("masksTransform", "Transformation masks",
         ["MM_MASK_DEKU", "MM_MASK_GORON", "MM_MASK_ZORA", "MM_MASK_FIERCE_DEITY",
          "SHARED_MASK_GORON", "SHARED_MASK_ZORA"], true),
        ("masksOot", "Ocarina of Time masks",
         ["OOT_MASK_KEATON", "OOT_MASK_SKULL", "OOT_MASK_SPOOKY", "OOT_MASK_BUNNY",
          "OOT_MASK_GORON", "OOT_MASK_ZORA", "OOT_MASK_GERUDO", "OOT_MASK_TRUTH",
          "OOT_MASK_BLAST", "OOT_MASK_STONE", "OOT_MASK_KAMARO", "SHARED_MASK_KEATON",
          "SHARED_MASK_BUNNY", "SHARED_MASK_TRUTH", "SHARED_MASK_GORON", "SHARED_MASK_ZORA",
          "SHARED_MASK_BLAST", "SHARED_MASK_STONE", "SHARED_MASK_KAMARO", "SHARED_MASK_SKULL",
          "SHARED_MASK_SPOOKY", "SHARED_MASK_GERUDO"], true),
        ("triforce", "Triforce pieces",
         ["SHARED_TRIFORCE", "SHARED_TRIFORCE_POWER", "SHARED_TRIFORCE_COURAGE",
          "SHARED_TRIFORCE_WISDOM", "OOT_TRIFORCE", "MM_TRIFORCE"], false),
        ("coinsRed", "Red coins", ["OOT_COIN_RED", "MM_COIN_RED"], false),
        ("coinsGreen", "Green coins", ["OOT_COIN_GREEN", "MM_COIN_GREEN"], false),
        ("coinsBlue", "Blue coins", ["OOT_COIN_BLUE", "MM_COIN_BLUE"], false),
        ("coinsYellow", "Yellow coins", ["OOT_COIN_YELLOW", "MM_COIN_YELLOW"], false),
    ];

    private const int ClockTowerRoofSongEvent = 12;
    private const int OathToOrder = 19;
    private const int GoronLullaby = 15;
    private const int LullabyIntro = 16;

    private static readonly (string Key, string Name, int Notes, string[] Buttons)[] MmSongs =
    [
        ("ZELDA", "Zelda's Lullaby", 6, ["C_UP", "C_RIGHT", "C_LEFT"]),
        ("EPONA", "Epona's Song", 6, ["C_UP", "C_RIGHT", "C_LEFT"]),
        ("SARIA", "Saria's Song", 6, ["C_DOWN", "C_RIGHT", "C_LEFT"]),
        ("STORMS", "Song of Storms", 6, ["A", "C_DOWN", "C_UP"]),
        ("SUN", "Sun's Song", 6, ["C_RIGHT", "C_DOWN", "C_UP"]),
        ("TIME", "Song of Time", 6, ["A", "C_RIGHT", "C_DOWN"]),
        ("TP_FOREST", "Minuet of Forest", 6, ["A", "C_UP", "C_RIGHT", "C_LEFT"]),
        ("TP_FIRE", "Bolero of Fire", 8, ["A", "C_RIGHT", "C_DOWN"]),
        ("TP_WATER", "Serenade of Water", 5, ["A", "C_RIGHT", "C_DOWN", "C_LEFT"]),
        ("TP_SPIRIT", "Requiem of Spirit", 6, ["A", "C_RIGHT", "C_DOWN"]),
        ("TP_SHADOW", "Nocturne of Shadow", 7, ["A", "C_RIGHT", "C_DOWN", "C_LEFT"]),
        ("TP_LIGHT", "Prelude of Light", 6, ["C_UP", "C_RIGHT", "C_LEFT"]),
        ("HEALING", "Song of Healing", 6, ["C_LEFT", "C_RIGHT", "C_DOWN"]),
        ("SOARING", "Song of Soaring", 6, ["C_DOWN", "C_UP", "C_LEFT"]),
        ("AWAKENING", "Sonata of Awakening", 7, ["C_UP", "C_RIGHT", "C_LEFT", "A"]),
        ("GORON", "Goron Lullaby", 8, ["A", "C_RIGHT", "C_LEFT"]),
        ("GORON_HALF", "Lullaby Intro", 6, ["A", "C_RIGHT", "C_LEFT"]),
        ("ZORA", "New Wave Bossa Nova", 7, ["C_LEFT", "C_RIGHT", "C_UP", "C_DOWN"]),
        ("EMPTINESS", "Elegy of Emptiness", 7, ["C_LEFT", "C_RIGHT", "C_UP", "C_DOWN"]),
        ("ORDER", "Oath to Order", 6, ["C_RIGHT", "C_DOWN", "C_UP", "A"]),
    ];

    private static readonly string[] SoulPrefixes =
        ["SOUL_ENEMY_", "SOUL_BOSS_", "SOUL_NPC_", "SOUL_ANIMAL_", "SOUL_MISC_"];

    private readonly Dictionary<string, int> _have;
    private readonly Dictionary<string, int> _pool = [];
    private readonly Dictionary<string, string> _names = [];

    public ChecksSummary Checks { get; }
    public ForeignSummary Foreign { get; }
    public IReadOnlyList<DungeonProgress> DungeonRows { get; }
    public IReadOnlyList<ItemGroupProgress> ItemGroups(Game game) => game == Game.Oot ? _groupsOot : _groupsMm;
    public IReadOnlyList<ItemProgress> Songs(Game game) => game == Game.Oot ? _songsOot : _songsMm;
    public IReadOnlyList<ItemProgress> RustyKeys(Game game) => game == Game.Oot ? _rustyOot : _rustyMm;
    public IReadOnlyList<ItemProgress> Souls(Game game, SoulSub sub) =>
        (game == Game.Oot ? _soulsOot : _soulsMm)[(int)sub];
    public IReadOnlyList<GoalProgress> Goals { get; }
    public ushort PlayerId { get; }

    /// <summary>Major items received, listed under each game they belong to; songs have no group.</summary>
    public IReadOnlyList<FoundItem> Found(Game game) => game == Game.Oot ? _foundOot : _foundMm;

    private readonly List<ItemGroupProgress> _groupsOot = [], _groupsMm = [];
    private readonly List<FoundItem> _foundOot = [], _foundMm = [];
    private readonly List<ItemProgress> _songsOot = [], _songsMm = [];
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

        foreach (var starting in seed.StartingItems)
        {
            AddToPool(starting.Item, starting.Count);
        }

        foreach (var (placement, _) in checks.Concat(foreignChecks))
        {
            if (placement.IsFor(seed.PlayerId) && placement.Item.Id.Length > 0)
            {
                AddToPool(placement.Item, 1);
            }
        }

        Checks = CountChecks(checks);
        Foreign = new ForeignSummary(
            foreignChecks.Count(c => c.Collected && c.Placement.IsFor(seed.PlayerId)),
            foreignChecks.Count(c => c.Placement.IsFor(seed.PlayerId)));
        DungeonRows = [.. Dungeons.Select(d => BuildDungeon(d, checks)).Where(d => d.HasItems || d.ChecksTotal > 0)];

        var groupsOot = new SortedDictionary<ItemGroup, List<ItemProgress>>();
        var groupsMm = new SortedDictionary<ItemGroup, List<ItemProgress>>();
        foreach (var (id, total) in _pool.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var item = MakeItem(id, total);
            var name = ItemGrouping.StripGame(id);
            var soul = Array.FindIndex(SoulPrefixes, p => name.StartsWith(p, StringComparison.Ordinal));
            if (soul >= 0)
            {
                AddPerGame(id, item, _soulsOot[soul], _soulsMm[soul]);
            }
            else if (name.StartsWith("SONG_", StringComparison.Ordinal))
            {
                if (!name.StartsWith("SONG_NOTE_", StringComparison.Ordinal))
                {
                    AddPerGame(id, item, _songsOot, _songsMm);
                }
            }
            else if (name.StartsWith("RUSTY_KEY_", StringComparison.Ordinal))
            {
                AddPerGame(id, item, _rustyOot, _rustyMm);
            }
            else if (ItemGrouping.Classify(id) is { } group)
            {
                AddPerGame(id, item, Bucket(groupsOot, group), Bucket(groupsMm, group));
            }
        }

        _groupsOot.AddRange(groupsOot.Where(g => g.Value.Count > 0).Select(g => new ItemGroupProgress(g.Key, g.Value)));
        _groupsMm.AddRange(groupsMm.Where(g => g.Value.Count > 0).Select(g => new ItemGroupProgress(g.Key, g.Value)));
        Goals = BuildGoals(seed);
        PlayerId = seed.PlayerId;
        foreach (var (placement, collected) in checks.Concat(foreignChecks))
        {
            if (collected && placement.IsFor(seed.PlayerId) && IsListedMajor(placement.Item, out var group))
            {
                var checkName = placement.Check.Name.Length > 0 ? placement.Check.Name
                    : seed.FindPlacement(placement.Check.Game, placement.Check.Id)?.Check.Name ?? placement.Check.Id;
                var itemName = placement.Item.Name.Length > 0 ? placement.Item.Name
                    : _names.GetValueOrDefault(placement.Item.Id, placement.Item.Id);
                AddPerGame(placement.Item.Id,
                           new FoundItem(placement.Item.Id, itemName, group, checkName, placement.SourcePlayer),
                           _foundOot, _foundMm);
            }
        }
    }

    private static bool IsListedMajor(ItemRef item, out ItemGroup? group)
    {
        group = null;
        if (!ContainerContents.IsMajorItem(item.Game, item.Id))
        {
            return false;
        }

        var name = ItemGrouping.StripGame(item.Id);
        if (name.StartsWith("SONG_", StringComparison.Ordinal))
        {
            return !name.StartsWith("SONG_NOTE_", StringComparison.Ordinal);
        }

        group = ItemGrouping.Classify(item.Id);
        return group is not (null or ItemGroup.Tokens);
    }

    private void AddToPool(ItemRef item, int count)
    {
        _pool[item.Id] = _pool.GetValueOrDefault(item.Id) + count;
        if (item.Name.Length > 0)
        {
            _names.TryAdd(item.Id, item.Name);
        }
    }

    private static ChecksSummary CountChecks(IReadOnlyList<(ItemPlacement Placement, bool Collected)> checks)
    {
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

        return new ChecksSummary(ootHave + mmHave, ootTotal + mmTotal, ootHave, ootTotal, mmHave, mmTotal);
    }

    private DungeonProgress BuildDungeon(
        DungeonDef def, IReadOnlyList<(ItemPlacement Placement, bool Collected)> checks)
    {
        var oot = def.Game == Game.Oot;
        var pre = oot ? "OOT_" : "MM_";
        var ringId = $"{pre}KEY_RING_{def.Token}";
        var bossId = $"{pre}BOSS_KEY_{def.Token}";
        var mapId = $"{pre}MAP_{def.Token}";
        var compassId = $"{pre}COMPASS_{def.Token}";
        var fairyId = $"MM_STRAY_FAIRY_{def.Token}";
        var smallKeyId = $"{pre}SMALL_KEY_{def.Token}";

        int checksHave = 0, checksTotal = 0;
        foreach (var (placement, collected) in checks)
        {
            var check = placement.Check;
            if (check.Game != def.Game)
            {
                continue;
            }

            var inScene = check.SceneId is { } scene && def.Scenes.Contains(scene);
            if (inScene || (def.CheckPrefix.Length > 0 && check.Id.StartsWith(def.CheckPrefix, StringComparison.Ordinal)))
            {
                checksTotal++;
                checksHave += collected ? 1 : 0;
            }
        }

        int silverHave = 0, silverTotal = 0;
        if (oot)
        {
            foreach (var (id, total) in _pool)
            {
                if (IsSilverFor(id, def.Token))
                {
                    silverTotal += total;
                    silverHave += Math.Min(Have(id), total);
                }
            }

            if (Have("OOT_RUPEE_MAGICAL") > 0)
            {
                silverHave = silverTotal;
            }
        }

        return new DungeonProgress(
            def.Name, def.Game,
            ChecksHave: checksHave, ChecksTotal: checksTotal,
            KeysHave: Math.Min(Have(smallKeyId), Pool(smallKeyId)), KeysTotal: Pool(smallKeyId),
            HasKeyRing: Pool(ringId) > 0, KeyRing: Have(ringId) > 0,
            SkeletonOwned: Have(oot ? "OOT_SKELETON_KEY" : "MM_SKELETON_KEY") > 0,
            HasMap: Pool(mapId) > 0, Map: Have(mapId) > 0,
            HasCompass: Pool(compassId) > 0, Compass: Have(compassId) > 0,
            HasBossKey: Pool(bossId) > 0, BossKey: Have(bossId) > 0,
            HasFairies: !oot && Pool(fairyId) > 0,
            FairiesHave: oot ? 0 : Math.Min(Have(fairyId), Pool(fairyId)), FairiesTotal: oot ? 0 : Pool(fairyId),
            SilverHave: silverHave, SilverTotal: silverTotal);
    }

    private static bool IsSilverFor(string id, string token)
    {
        foreach (var family in new[] { "OOT_POUCH_SILVER_", "OOT_RUPEE_SILVER_" })
        {
            if (id.StartsWith(family, StringComparison.Ordinal))
            {
                var rest = id[family.Length..];
                return rest == token || rest.StartsWith(token + "_", StringComparison.Ordinal);
            }
        }

        return false;
    }

    private IReadOnlyList<GoalProgress> BuildGoals(Seed seed)
    {
        var goal = seed.GetStringSetting("goal", "both");
        var bosses = goal is not ("triforce" or "triforce3");
        var games = seed.GetStringSetting("games", "ootmm");
        var goals = new List<GoalProgress>();

        switch (goal)
        {
            case "triforce":
                goals.Add(new GoalProgress("TRIFORCE", "Triforce Hunt", Have("SHARED_TRIFORCE"),
                                           seed.GetIntSetting("triforceGoal", 20), [], []));
                break;
            case "triforce3":
                string[] named = ["SHARED_TRIFORCE_POWER", "SHARED_TRIFORCE_COURAGE", "SHARED_TRIFORCE_WISDOM"];
                goals.Add(new GoalProgress("TRIFORCE", "Triforce Quest", named.Count(id => Have(id) > 0),
                                           named.Length, [], []));
                break;
        }

        if (games != "mm")
        {
            var bridge = seed.GetStringSetting("rainbowBridge", "medallions");
            AddGoal(goals, seed, "BRIDGE", "Rainbow Bridge", bridge == "custom", bridge switch
            {
                "vanilla" =>
                [
                    SpiritAndShadow(),
                    new GoalPart("Light Arrows", AnyOwned("OOT_ARROW_LIGHT", "SHARED_ARROW_LIGHT"), 1),
                ],
                "medallions" => [Distinct("Medallions", Medallions)],
                _ => [],
            });

            var lacs = seed.GetStringSetting("lacs", "vanilla");
            List<GoalPart> lacsNeeds = lacs == "custom" ? [] : [SpiritAndShadow()];
            if (seed.GetBoolSetting("soulsNpcOot"))
            {
                lacsNeeds.Add(new GoalPart("Soul of Sheik", AnyOwned("OOT_SOUL_NPC_SHEIK"), 1));
                lacsNeeds.Add(new GoalPart("Soul of Zelda", AnyOwned("OOT_SOUL_NPC_ZELDA"), 1));
            }

            AddGoal(goals, seed, "LACS", "Light Arrow cutscene", lacs == "custom", lacsNeeds);

            if (bosses)
            {
                var key = seed.GetStringSetting("ganonBossKey", "removed");
                AddGoal(goals, seed, "GANON_BK", "Ganon's Boss Key", key == "custom",
                        key is "removed" or "custom"
                            ? []
                            : [new GoalPart("Ganon's Boss Key", AnyOwned("OOT_BOSS_KEY_GANON"), 1)]);
            }
        }

        if (games != "oot")
        {
            var moon = seed.GetStringSetting("moon", "vanilla");
            List<GoalPart> moonNeeds = moon == "vanilla" ? [Distinct("Boss Remains", Remains)] : [];
            if (!seed.GetBoolSetting("openMoon"))
            {
                moonNeeds.AddRange(RoofSong(seed));
            }

            AddGoal(goals, seed, "MOON", "Moon access", moon == "custom", moonNeeds);

            if (bosses)
            {
                AddGoal(goals, seed, "MAJORA", "Majora fight", seed.GetStringSetting("majoraChild", "none") == "custom",
                        seed.GetBoolSetting("soulsNpcMm")
                            ? [new GoalPart("Soul of Moon Children", AnyOwned("MM_SOUL_NPC_MOON_CHILDREN"), 1)]
                            : []);
            }
        }

        return goals;
    }

    private void AddGoal(
        List<GoalProgress> goals, Seed seed, string name, string title, bool custom, List<GoalPart> needs)
    {
        var parts = new List<GoalPart>();
        var owned = 0;
        var threshold = 0;
        if (custom && seed.SpecialConds.TryGetValue(name, out var cond) && cond.Count > 0)
        {
            threshold = cond.Count;
            var uniqueOwned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (field, partName, ids, unique) in GoalFields)
            {
                if (!cond.Field(field))
                {
                    continue;
                }

                if (unique)
                {
                    parts.Add(new GoalPart(partName, ids.Count(id => Have(id) > 0), ids.Count(id => Pool(id) > 0)));
                    owned += ids.Count(id => Have(id) > 0 && uniqueOwned.Add(id));
                }
                else
                {
                    parts.Add(new GoalPart(partName, ids.Sum(Have), ids.Sum(Pool)));
                    owned += ids.Sum(Have);
                }
            }
        }

        if (threshold > 0 || needs.Count > 0)
        {
            goals.Add(new GoalProgress(name, title, owned, threshold, parts, needs));
        }
    }

    private int AnyOwned(params string[] ids) => ids.Any(id => Have(id) > 0) ? 1 : 0;

    private GoalPart Distinct(string name, string[] ids) => new(name, ids.Count(id => Have(id) > 0), ids.Length);

    private GoalPart SpiritAndShadow() =>
        Distinct("Spirit and Shadow Medallions", ["OOT_MEDALLION_SPIRIT", "OOT_MEDALLION_SHADOW"]);

    private IEnumerable<GoalPart> RoofSong(Seed seed)
    {
        var song = seed.SongEventsMm.Count > ClockTowerRoofSongEvent ? seed.SongEventsMm[ClockTowerRoofSongEvent]
                                                                      : OathToOrder;
        var (key, name, notes, buttons) = MmSongs[song];
        var owned = song switch
        {
            GoronLullaby => GoronOwned(),
            LullabyIntro => GoronOwned() || (seed.GetStringSetting("progressiveGoronLullabyMm", "progressive")
                                                 == "progressive" && SongOwned(key, "GORON", notes)),
            _ => SongOwned(key, key, notes),
        };
        yield return new GoalPart($"{name} on the Clock Tower roof", owned ? 1 : 0, 1);

        var ocarinas = seed.GetBoolSetting("sharedOcarina") && !seed.GetBoolSetting("fairyOcarinaMm") ? 2 : 1;
        var ocarina = Have("MM_OCARINA") >= ocarinas || Have("SHARED_OCARINA") >= ocarinas;
        yield return new GoalPart("Ocarina", ocarina ? 1 : 0, 1);

        if (seed.GetBoolSetting("ocarinaButtonsShuffleMm"))
        {
            var prefix = seed.GetBoolSetting("sharedOcarinaButtons") ? "SHARED_BUTTON_" : "MM_BUTTON_";
            yield return new GoalPart($"Ocarina buttons for {name}", buttons.Count(b => Have(prefix + b) > 0),
                                      buttons.Length);
        }
    }

    private bool GoronOwned() =>
        SongOwned("GORON", "GORON", 8) || Have("MM_SONG_GORON_HALF") >= 2 || Have("SHARED_SONG_GORON_HALF") >= 2;

    private bool SongOwned(string key, string noteKey, int notes) =>
        Have($"MM_SONG_{key}") > 0 || Have($"SHARED_SONG_{key}") > 0
        || Have($"MM_SONG_NOTE_{noteKey}") >= notes || Have($"SHARED_SONG_NOTE_{noteKey}") >= notes;

    private static List<ItemProgress> Bucket(SortedDictionary<ItemGroup, List<ItemProgress>> groups, ItemGroup group)
    {
        if (!groups.TryGetValue(group, out var list))
        {
            groups[group] = list = [];
        }

        return list;
    }

    private static void AddPerGame<T>(string id, T item, List<T> oot, List<T> mm)
    {
        if (!id.StartsWith("MM_", StringComparison.Ordinal))
        {
            oot.Add(item);
        }

        if (!id.StartsWith("OOT_", StringComparison.Ordinal))
        {
            mm.Add(item);
        }
    }

    private int Have(string id) => _have.GetValueOrDefault(id);

    private int Pool(string id) => _pool.GetValueOrDefault(id);

    private ItemProgress MakeItem(string id, int total) =>
        new(id, _names.TryGetValue(id, out var name) ? name : id.Replace('_', ' '), Math.Min(Have(id), total), total);
}
