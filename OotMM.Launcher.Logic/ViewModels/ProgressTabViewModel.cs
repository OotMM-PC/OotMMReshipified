using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using OotMM.Core;

namespace OotMM.Launcher.ViewModels;

public sealed record ProgressSection(int Key, string Label);

public sealed record ProgressMeter(string Label, string CountText, double Fraction, bool Done, int Section);

public sealed record GoalRowViewModel(string Name, string CountText, double Fraction, bool Satisfied, string Detail);

public sealed record StatusChip(string Text, bool Ok);

public sealed record ProgressRow(bool IsHeader, string Text, string CountText, int Level, string Detail = "");

public sealed record DungeonRowViewModel(
    bool IsHeader, string Name, string ChecksText, bool Done, ObservableCollection<StatusChip> Chips);

public sealed partial class ProgressTabViewModel : ViewModelBase
{
    private const int Overview = 0;
    private const int ItemsSection = 1;
    private const int SongsSection = 2;
    private const int DungeonsSection = 3;
    private const int SoulsSection = 4;
    private const int FoundSection = 5;

    private const int Missing = 0;
    private const int Partial = 1;
    private const int Complete = 2;

    private ProgressReport? _report;
    private string _goal = "";
    private bool _multiworld;
    private bool _ganonDefeated;
    private bool _majoraDefeated;
    private int _sectionIndex;
    private int _gameIndex;
    private bool _missingOnly;
    private string _foundFilter = "";
    private string _dungeonSignature = "";

    public int SectionIndex
    {
        get => _sectionIndex;
        set
        {
            if (Set(ref _sectionIndex, Sections.Any(s => s.Key == value) ? value : Overview))
            {
                RaisePageStates();
                RefreshRows();
            }
        }
    }

    public int GameIndex
    {
        get => _gameIndex;
        set
        {
            if (Set(ref _gameIndex, Math.Clamp(value, 0, 1)))
            {
                RefreshRows();
            }
        }
    }

    public bool MissingOnly
    {
        get => _missingOnly;
        set
        {
            if (Set(ref _missingOnly, value))
            {
                RefreshRows();
            }
        }
    }

    /// <summary>Text that narrows Found to items whose name, finder or check contains it.</summary>
    public string FoundFilter
    {
        get => _foundFilter;
        set
        {
            if (Set(ref _foundFilter, value ?? ""))
            {
                RefreshRows();
            }
        }
    }

    public ObservableCollection<ProgressSection> Sections { get; private set; } = [new(Overview, "Overview")];

    public bool ShowOverview => SectionIndex == Overview;
    public bool ShowDungeons => SectionIndex == DungeonsSection;
    public bool ShowList => SectionIndex is ItemsSection or SongsSection or SoulsSection or FoundSection;
    public bool ShowMissingOnly => SectionIndex != FoundSection;
    public bool ShowFoundFilter => SectionIndex == FoundSection;

    public ObservableCollection<ProgressMeter> CheckMeters { get; private set; } = [];
    public string GoalText { get; private set; } = "";
    public ObservableCollection<StatusChip> Bosses { get; private set; } = [];
    public ObservableCollection<GoalRowViewModel> Goals { get; private set; } = [];
    public bool HasGoals => Goals.Count > 0;
    public ObservableCollection<ProgressMeter> Collection { get; private set; } = [];

    public ObservableCollection<DungeonRowViewModel> Dungeons { get; private set; } = [];

    public ObservableCollection<ProgressRow> Rows { get; private set; } = [];
    public bool HasRows => Rows.Count > 0;
    public string EmptyText => SectionIndex == FoundSection
        ? _foundFilter.Trim().Length > 0 ? "No found items match." : "No major items found yet."
        : MissingOnly ? "Nothing left to find here." : "This seed has none of these.";

    public void OpenSection(int section) => SectionIndex = section;

    internal void FollowGame(Game game) => GameIndex = game == Game.Oot ? 0 : 1;

    public void SetReport(ProgressReport report, string goal, bool multiworld, bool ganonDefeated, bool majoraDefeated)
    {
        _report = report;
        _goal = goal;
        _multiworld = multiworld;
        _ganonDefeated = ganonDefeated;
        _majoraDefeated = majoraDefeated;

        var sections = new List<ProgressSection> { new(Overview, "Overview"), new(ItemsSection, "Items") };
        if (Both(report.Songs).Any())
        {
            sections.Add(new ProgressSection(SongsSection, "Songs"));
        }

        if (report.DungeonRows.Count > 0)
        {
            sections.Add(new ProgressSection(DungeonsSection, "Dungeons"));
        }

        if (AllSouls(report).Any())
        {
            sections.Add(new ProgressSection(SoulsSection, "Souls"));
        }

        sections.Add(new ProgressSection(FoundSection, "Found"));

        if (!sections.SequenceEqual(Sections))
        {
            Sections = new ObservableCollection<ProgressSection>(sections);
            Raise(nameof(Sections));
            SectionIndex = SectionIndex;
        }

        RefreshOverview(report);
        RefreshDungeons(report);
        RefreshRows();
    }

    private void RaisePageStates()
    {
        Raise(nameof(ShowOverview));
        Raise(nameof(ShowDungeons));
        Raise(nameof(ShowList));
        Raise(nameof(ShowMissingOnly));
        Raise(nameof(ShowFoundFilter));
    }

    private static IEnumerable<ItemProgress> Both(Func<Game, IReadOnlyList<ItemProgress>> list) =>
        list(Game.Oot).Concat(list(Game.Mm)).DistinctBy(i => i.Id);

    private static IEnumerable<ItemProgress> AllSouls(ProgressReport report) =>
        Enum.GetValues<SoulSub>().SelectMany(sub => Both(game => report.Souls(game, sub)));

    private static IEnumerable<ItemProgress> AllItems(ProgressReport report) =>
        Both(game => [.. report.ItemGroups(game).SelectMany(g => g.Items), .. report.RustyKeys(game)]);

    private static string Count(int have, int total) => $"{have} / {total}";

    private static double Fraction(int have, int total) => total > 0 ? Math.Clamp((double)have / total, 0, 1) : 0;

    private static ProgressMeter Meter(string label, int have, int total, int section = Overview) =>
        new(label, Count(have, total), Fraction(have, total), total > 0 && have >= total, section);

    private static ProgressMeter Meter(string label, IEnumerable<ItemProgress> items, int section)
    {
        var list = items.ToList();
        return Meter(label, list.Count(i => i.Complete), list.Count, section);
    }

    private static GoalRowViewModel GoalRow(GoalProgress goal)
    {
        var have = Math.Min(goal.Owned, goal.Threshold) + goal.Needs.Sum(n => Math.Min(n.Have, n.Total));
        var total = goal.Threshold + goal.Needs.Sum(n => n.Total);
        var lines = new List<string>();
        if (goal.Parts.Count > 0)
        {
            lines.Add($"Any {goal.Threshold} of: " +
                      string.Join("  ·  ", goal.Parts.Select(p => $"{p.Name} {Count(p.Have, p.Total)}")));
        }

        if (goal.Needs.Count > 0)
        {
            lines.Add((goal.Parts.Count > 0 ? "Also needs: " : "Needs: ") +
                      string.Join("  ·  ", goal.Needs.Select(n => n.Total == 1
                          ? $"{n.Name} {(n.Have >= 1 ? "✓" : "○")}"
                          : $"{n.Name} {Count(n.Have, n.Total)}")));
        }

        return new GoalRowViewModel(goal.Title, Count(have, total), Fraction(have, total), goal.Satisfied,
                                    string.Join("\n", lines));
    }

    private void RefreshOverview(ProgressReport report)
    {
        var checks = new List<ProgressMeter> { Meter("All checks", report.Checks.Have, report.Checks.Total) };
        if (report.Checks.OotTotal > 0 && report.Checks.MmTotal > 0)
        {
            checks.Add(Meter("Ocarina of Time", report.Checks.OotHave, report.Checks.OotTotal));
            checks.Add(Meter("Majora's Mask", report.Checks.MmHave, report.Checks.MmTotal));
        }

        if (_multiworld && report.Foreign.Total > 0)
        {
            checks.Add(Meter("Your items found by other players", report.Foreign.Have, report.Foreign.Total));
        }

        if (!checks.SequenceEqual(CheckMeters))
        {
            CheckMeters = new ObservableCollection<ProgressMeter>(checks);
            Raise(nameof(CheckMeters));
        }

        var goalText = _goal switch
        {
            "any" => "Defeat Ganon or Majora to finish.",
            "ganon" => "Defeat Ganon to finish.",
            "majora" => "Defeat Majora to finish.",
            "both" => "Defeat Ganon and Majora to finish.",
            "triforce" => "Collect enough Triforce pieces to finish.",
            "triforce3" => "Collect the three parts of the Triforce to finish.",
            _ => "",
        };
        if (goalText != GoalText)
        {
            GoalText = goalText;
            Raise(nameof(GoalText));
        }

        var bosses = new List<StatusChip>();
        if (_goal is "any" or "ganon" or "both")
        {
            bosses.Add(new StatusChip(_ganonDefeated ? "Ganon defeated" : "Ganon not defeated", _ganonDefeated));
        }

        if (_goal is "any" or "majora" or "both")
        {
            bosses.Add(new StatusChip(_majoraDefeated ? "Majora defeated" : "Majora not defeated", _majoraDefeated));
        }

        if (!bosses.SequenceEqual(Bosses))
        {
            Bosses = new ObservableCollection<StatusChip>(bosses);
            Raise(nameof(Bosses));
        }

        var goals = report.Goals.Select(GoalRow).ToList();
        if (!goals.SequenceEqual(Goals))
        {
            Goals = new ObservableCollection<GoalRowViewModel>(goals);
            Raise(nameof(Goals));
            Raise(nameof(HasGoals));
        }

        var collection = new List<ProgressMeter> { Meter("Items", AllItems(report), ItemsSection) };
        if (Sections.Any(s => s.Key == SongsSection))
        {
            collection.Add(Meter("Songs", Both(report.Songs), SongsSection));
        }

        if (Sections.Any(s => s.Key == DungeonsSection))
        {
            var withChecks = report.DungeonRows.Where(d => d.ChecksTotal > 0).ToList();
            collection.Add(Meter("Dungeons cleared", withChecks.Count(d => d.ChecksHave >= d.ChecksTotal),
                                 withChecks.Count, DungeonsSection));
        }

        if (Sections.Any(s => s.Key == SoulsSection))
        {
            collection.Add(Meter("Souls", AllSouls(report), SoulsSection));
        }

        if (!collection.SequenceEqual(Collection))
        {
            Collection = new ObservableCollection<ProgressMeter>(collection);
            Raise(nameof(Collection));
        }
    }

    private void RefreshDungeons(ProgressReport report)
    {
        var rows = new List<DungeonRowViewModel>();
        var bothGames = report.DungeonRows.Select(d => d.Game).Distinct().Count() > 1;
        Game? current = null;
        foreach (var d in report.DungeonRows)
        {
            if (bothGames && current != d.Game)
            {
                current = d.Game;
                rows.Add(new DungeonRowViewModel(true, GameInfo.Title(d.Game), "", false, []));
            }

            rows.Add(new DungeonRowViewModel(false, d.Name,
                                             d.ChecksTotal > 0 ? Count(d.ChecksHave, d.ChecksTotal) : "",
                                             d.ChecksTotal > 0 && d.ChecksHave >= d.ChecksTotal,
                                             new ObservableCollection<StatusChip>(DungeonChips(d))));
        }

        var signature = string.Join('\n', rows.Select(r =>
            $"{r.IsHeader}|{r.Name}|{r.ChecksText}|{string.Join(',', r.Chips.Select(c => c.Text + c.Ok))}"));
        if (signature != _dungeonSignature)
        {
            _dungeonSignature = signature;
            Dungeons = new ObservableCollection<DungeonRowViewModel>(rows);
            Raise(nameof(Dungeons));
        }
    }

    private static List<StatusChip> DungeonChips(DungeonProgress d)
    {
        var chips = new List<StatusChip>();
        if (d.KeysTotal > 0 || d.HasKeyRing)
        {
            chips.Add(d.SkeletonOwned ? new StatusChip("Skeleton key", true)
                : d.KeysTotal > 0 ? new StatusChip($"Small keys {d.KeysHave}/{d.KeysTotal}", d.KeysHave >= d.KeysTotal)
                : new StatusChip("Key ring", d.KeyRing));
        }

        if (d.HasBossKey)
        {
            chips.Add(new StatusChip("Boss key", d.BossKey));
        }

        if (d.HasMap)
        {
            chips.Add(new StatusChip("Map", d.Map));
        }

        if (d.HasCompass)
        {
            chips.Add(new StatusChip("Compass", d.Compass));
        }

        if (d.HasFairies)
        {
            chips.Add(new StatusChip($"Fairies {d.FairiesHave}/{d.FairiesTotal}", d.FairiesHave >= d.FairiesTotal));
        }

        if (d.SilverTotal > 0)
        {
            chips.Add(new StatusChip($"Silver rupees {d.SilverHave}/{d.SilverTotal}", d.SilverHave >= d.SilverTotal));
        }

        return chips;
    }

    private void RefreshRows()
    {
        var rows = new List<ProgressRow>();
        if (_report is { } report && ShowList)
        {
            var game = GameIndex == 0 ? Game.Oot : Game.Mm;
            if (SectionIndex == FoundSection)
            {
                AddFoundRows(rows, report, game);
            }
            else
            {
                AddListRows(rows, report, game);
            }
        }

        if (!rows.SequenceEqual(Rows))
        {
            Rows = new ObservableCollection<ProgressRow>(rows);
            Raise(nameof(Rows));
        }

        Raise(nameof(HasRows));
        Raise(nameof(EmptyText));
    }

    private void AddListRows(List<ProgressRow> rows, ProgressReport report, Game game)
    {
        IEnumerable<(string Title, IReadOnlyList<ItemProgress> Items)> groups = SectionIndex switch
        {
            ItemsSection => report.ItemGroups(game).Select(g => (GroupTitle(g.Group), g.Items))
                .Append(("Rusty keys", report.RustyKeys(game))),
            SongsSection => [("Songs", report.Songs(game))],
            _ => Enum.GetValues<SoulSub>().Select(sub => (SoulTitle(sub), report.Souls(game, sub))),
        };
        foreach (var (title, items) in groups)
        {
            var shown = items.Where(i => !MissingOnly || !i.Complete)
                .Select(i => (Name: ShortName(i.Name), Item: i))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            var complete = items.Count(i => i.Complete);
            rows.Add(new ProgressRow(true, title, Count(complete, items.Count),
                                     complete >= items.Count ? Complete : Missing));
            rows.AddRange(shown.Select(e => new ProgressRow(
                false, e.Name,
                e.Item.Total > 1 ? Count(e.Item.Have, e.Item.Total) : "",
                e.Item.Complete ? Complete : e.Item.Owned ? Partial : Missing)));
        }
    }

    private void AddFoundRows(List<ProgressRow> rows, ProgressReport report, Game game)
    {
        var filter = _foundFilter.Trim();
        var groups = report.Found(game)
            .Select(f => (Name: ShortName(f.ItemName), Item: f,
                          Detail: $"{(f.Finder == report.PlayerId ? "You" : $"Player {f.Finder}")} · {f.CheckName}"))
            .Where(e => filter.Length == 0 || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || e.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Item.Group)
            .OrderBy(g => FoundGroupOrder(g.Key));
        foreach (var group in groups)
        {
            var items = group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Item.CheckName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            rows.Add(new ProgressRow(true, group.Key is { } key ? GroupTitle(key) : "Songs", items.Count.ToString(),
                                     Complete));
            rows.AddRange(items.Select(e => new ProgressRow(false, e.Name, "", Complete, e.Detail)));
        }
    }

    /// <summary>Where a group sits in Found; songs, which have no group, come right after Items.</summary>
    private static int FoundGroupOrder(ItemGroup? group) =>
        group is { } known ? (int)known * 2 : (int)ItemGroup.Items * 2 + 1;

    private static string GroupTitle(ItemGroup group) => group switch
    {
        ItemGroup.Quest => "Goal items",
        ItemGroup.Equipment => "Equipment and upgrades",
        ItemGroup.Items => "Items",
        ItemGroup.Masks => "Masks",
        ItemGroup.Bottles => "Bottles",
        ItemGroup.Trade => "Trade and quest items",
        ItemGroup.Buttons => "Buttons",
        ItemGroup.Travel => "Owls, clocks and maps",
        ItemGroup.Tokens => "Tokens and fairies",
        _ => "Other",
    };

    private static string SoulTitle(SoulSub sub) => sub switch
    {
        SoulSub.Enemy => "Enemy souls",
        SoulSub.Boss => "Boss souls",
        SoulSub.Npc => "Character souls",
        SoulSub.Animal => "Animal souls",
        _ => "Other souls",
    };

    [GeneratedRegex(@"\s*\((?:OoT|OOT|MM)\)$")]
    private static partial Regex GameSuffix();

    [GeneratedRegex(@"^Rusty Key \((.+)\)$")]
    private static partial Regex RustyKeyName();

    [GeneratedRegex(@"^Soul of (?:the )?(.+)$")]
    private static partial Regex SoulName();

    internal static string ShortName(string name)
    {
        name = GameSuffix().Replace(name, "");
        if (name.StartsWith("Progressive ", StringComparison.Ordinal))
        {
            name = name["Progressive ".Length..];
        }

        if (RustyKeyName().Match(name) is { Success: true } rusty)
        {
            return rusty.Groups[1].Value;
        }

        if (SoulName().Match(name) is { Success: true } soul)
        {
            var rest = soul.Groups[1].Value;
            return char.ToUpperInvariant(rest[0]) + rest[1..];
        }

        return name;
    }

    public void LoadSampleData()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "preview",
            PlayerCount = 2,
            StringSettings = new Dictionary<string, string>
            {
                ["rainbowBridge"] = "custom", ["majoraChild"] = "custom",
            },
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["BRIDGE"] = new(4, new Dictionary<string, bool> { ["stones"] = true, ["medallions"] = true }),
                ["MAJORA"] = new(2, new Dictionary<string, bool> { ["remains"] = true }),
            },
        };
        (Game Game, uint Scene, string Item, string Name, bool Collected)[] sample =
        [
            (Game.Oot, 0x00, "OOT_STONE_EMERALD", "Kokiri's Emerald", true),
            (Game.Oot, 0x00, "OOT_MAP_DT", "Map (Deku Tree)", true),
            (Game.Oot, 0x00, "OOT_COMPASS_DT", "Compass (Deku Tree)", true),
            (Game.Oot, 0x03, "OOT_SMALL_KEY_FOREST", "Small Key (Forest)", true),
            (Game.Oot, 0x03, "OOT_SMALL_KEY_FOREST", "Small Key (Forest)", false),
            (Game.Oot, 0x03, "OOT_BOSS_KEY_FOREST", "Boss Key (Forest Temple)", false),
            (Game.Oot, 0x03, "OOT_MAP_FOREST", "Map (Forest Temple)", true),
            (Game.Oot, 0x03, "OOT_MEDALLION_FOREST", "Forest Medallion", false),
            (Game.Oot, 0x51, "OOT_STONE_RUBY", "Goron's Ruby", true),
            (Game.Oot, 0x51, "OOT_MEDALLION_FIRE", "Fire Medallion", false),
            (Game.Oot, 0x51, "OOT_HOOKSHOT", "Progressive Hookshot (OoT)", true),
            (Game.Oot, 0x51, "OOT_HOOKSHOT", "Progressive Hookshot (OoT)", false),
            (Game.Oot, 0x51, "OOT_BOW", "Fairy Bow (OoT)", true),
            (Game.Oot, 0x51, "OOT_SWORD_MASTER", "Master Sword", true),
            (Game.Oot, 0x51, "OOT_SHIELD_MIRROR", "Mirror Shield (OoT)", false),
            (Game.Oot, 0x51, "OOT_WALLET", "Progressive Wallet (OoT)", true),
            (Game.Oot, 0x51, "OOT_WALLET", "Progressive Wallet (OoT)", false),
            (Game.Oot, 0x51, "OOT_BOTTLE_EMPTY", "Empty Bottle (OoT)", true),
            (Game.Oot, 0x51, "OOT_WEIRD_EGG", "Weird Egg", false),
            (Game.Oot, 0x51, "OOT_SONG_ZELDA", "Zelda's Lullaby (OoT)", true),
            (Game.Oot, 0x51, "OOT_SONG_SARIA", "Saria's Song (OoT)", false),
            (Game.Oot, 0x51, "SHARED_SONG_EPONA", "Epona's Song", true),
            (Game.Oot, 0x51, "OOT_SOUL_ENEMY_STALFOS", "Soul of Stalfos", true),
            (Game.Oot, 0x51, "OOT_SOUL_BOSS_QUEEN_GOHMA", "Soul of Queen Gohma", false),
            (Game.Oot, 0x51, "SHARED_SOUL_NPC_MALON", "Soul of Malon", false),
            (Game.Oot, 0x51, "OOT_RUSTY_KEY_GUARD_HOUSE", "Rusty Key (Guard House)", true),
            (Game.Oot, 0x51, "OOT_GS_TOKEN", "Gold Skulltula Token", true),
            (Game.Oot, 0x51, "OOT_GS_TOKEN", "Gold Skulltula Token", false),
            (Game.Mm, 0x1B, "MM_KEY_RING_WF", "Key Ring (Woodfall)", true),
            (Game.Mm, 0x1B, "MM_BOSS_KEY_WF", "Boss Key (Woodfall Temple)", true),
            (Game.Mm, 0x1B, "MM_STRAY_FAIRY_WF", "Stray Fairy (Woodfall Temple)", true),
            (Game.Mm, 0x1B, "MM_STRAY_FAIRY_WF", "Stray Fairy (Woodfall Temple)", false),
            (Game.Mm, 0x1F, "MM_REMAINS_ODOLWA", "Odolwa's Remains", true),
            (Game.Mm, 0x21, "MM_KEY_RING_SH", "Key Ring (Snowhead)", false),
            (Game.Mm, 0x21, "MM_REMAINS_GOHT", "Goht's Remains", false),
            (Game.Mm, 0x6F, "MM_MASK_DEKU", "Deku Mask", true),
            (Game.Mm, 0x6F, "MM_MASK_BUNNY", "Bunny Hood (MM)", false),
            (Game.Mm, 0x6F, "MM_SONG_SOARING", "Song of Soaring (MM)", true),
            (Game.Mm, 0x6F, "MM_SOUL_ENEMY_OCTOROK", "Soul of Octoroks (MM)", false),
            (Game.Mm, 0x6F, "MM_OWL_CLOCK_TOWN", "Owl Statue (Clock Town)", true),
            (Game.Mm, 0x6F, "MM_ARROWS_10", "10 Arrows (MM)", true),
        ];
        var placements = sample.Select((s, index) => (
            new ItemPlacement(
                new CheckRef(s.Game, $"C{index}", $"C{index}", [], s.Scene, "", null),
                new ItemRef(s.Item.StartsWith("MM_", StringComparison.Ordinal) ? Game.Mm : Game.Oot, s.Item, s.Name),
                null, 1, 1, false),
            s.Collected)).ToList();
        var counts = placements.Where(p => p.Collected)
            .GroupBy(p => p.Item1.Item.Id)
            .ToDictionary(g => g.Key, g => g.Count());
        var foreign = new List<(ItemPlacement, bool)>
        {
            (new ItemPlacement(new CheckRef(Game.Oot, "F1", "F1", [], null, "", null),
                               new ItemRef(Game.Mm, "MM_MASK_GORON", "Goron Mask"), null, 1, 2, false), true),
        };
        SetReport(ProgressReport.Build(seed, placements, counts, foreign), "both", true, false, false);
    }
}
