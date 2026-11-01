using System.Collections.ObjectModel;
using OotMM.Core;

namespace OotMM.Launcher.ViewModels;

public sealed record ProgressItemRow(string Name, bool Owned)
{
    public string Mark => Owned ? "✓" : "○";
}

public sealed record GoalRowViewModel(string Name, string Text, bool Active, bool Satisfied)
{
    public bool Open => Active && !Satisfied;
}

public sealed record DungeonChip(string Text, bool Ok);

public sealed class DungeonRowViewModel(DungeonProgress d)
{
    public string GameTag { get; } = d.Game == Game.Oot ? "OOT" : "MM";
    public string Name { get; } = d.Name;
    public ObservableCollection<DungeonChip> Chips { get; } = new(BuildChips(d));

    private static List<DungeonChip> BuildChips(DungeonProgress d)
    {
        var chips = new List<DungeonChip>();
        if (d.SkeletonOwned)
        {
            chips.Add(new DungeonChip("Keys ✓", true));
        }
        else if (d.KeysTotal > 0)
        {
            chips.Add(new DungeonChip($"Keys {d.KeysHave}/{d.KeysTotal}", d.KeysHave >= d.KeysTotal));
        }
        else if (d.HasKeyRing)
        {
            chips.Add(new DungeonChip(d.KeyRing ? "Ring ✓" : "Ring ✗", d.KeyRing));
        }

        if (d.HasMap)
        {
            chips.Add(new DungeonChip(d.Map ? "Map ✓" : "Map ✗", d.Map));
        }

        if (d.HasCompass)
        {
            chips.Add(new DungeonChip(d.Compass ? "Comp ✓" : "Comp ✗", d.Compass));
        }

        if (d.HasBossKey)
        {
            chips.Add(new DungeonChip(d.BossKey ? "BK ✓" : "BK ✗", d.BossKey));
        }

        if (d.HasFairies)
        {
            chips.Add(new DungeonChip($"Fairies {d.FairiesHave}/{d.FairiesTotal}",
                                      d.FairiesHave >= d.FairiesTotal));
        }

        return chips;
    }
}

public sealed record SoulGroupViewModel(string Title, ObservableCollection<ProgressItemRow> Items);

public sealed class ProgressTabViewModel : ViewModelBase
{
    private ProgressReport? _report;
    private int _playerId;
    private int _playerCount;
    private string _songsMode = "";
    private int _categoryIndex;
    private int _gameIndex;

    public int CategoryIndex
    {
        get => _categoryIndex;
        set
        {
            if (Set(ref _categoryIndex, Math.Clamp(value, 0, 5)))
            {
                RefreshCategory();
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
                RefreshCategory();
            }
        }
    }

    public bool ShowOverview => CategoryIndex == 0;
    public bool ShowDungeons => CategoryIndex == 1;
    public bool ShowSouls => CategoryIndex == 2;
    public bool ShowItemList => CategoryIndex >= 3;
    public bool ShowGameRadio => CategoryIndex >= 2;
    public bool ShowSongsMode => CategoryIndex == 3;

    public string ChecksText { get; private set; } = "";
    public string ItemsOwnedText { get; private set; } = "";
    public string OotChecksText { get; private set; } = "";
    public string MmChecksText { get; private set; } = "";
    public string MultiworldText { get; private set; } = "";
    public bool ShowMultiworldText => MultiworldText.Length > 0;
    public ObservableCollection<GoalRowViewModel> Goals { get; } = [];
    public bool HasGoals => Goals.Count > 0;

    public string GoalsNote =>
        "Goal progress counts the families the ports also gate on (stones / medallions / remains / skulls / stray fairies).";

    public ObservableCollection<DungeonRowViewModel> Dungeons { get; } = [];
    public ObservableCollection<SoulGroupViewModel> SoulGroups { get; } = [];
    public ObservableCollection<ProgressItemRow> Items { get; } = [];
    public string ItemsHeader { get; private set; } = "";
    public string SongsModeText => $"mode: {_songsMode}";

    public void SetReport(ProgressReport report, int playerId, int playerCount, string songsMode)
    {
        _report = report;
        _playerId = playerId;
        _playerCount = playerCount;
        _songsMode = songsMode;
        RefreshCategory();
    }

    private void RefreshCategory()
    {
        Raise(nameof(ShowOverview));
        Raise(nameof(ShowDungeons));
        Raise(nameof(ShowSouls));
        Raise(nameof(ShowItemList));
        Raise(nameof(ShowGameRadio));
        Raise(nameof(ShowSongsMode));
        Raise(nameof(SongsModeText));
        if (_report is not { } report)
        {
            return;
        }

        var game = GameIndex == 0 ? Game.Oot : Game.Mm;
        switch (CategoryIndex)
        {
            case 0:
                ChecksText = $"Total: {report.Checks.Have} / {report.Checks.Total}";
                ItemsOwnedText = $"Items collected: {report.ItemsOwned}";
                OotChecksText = $"[OoT] {report.Checks.OotHave} / {report.Checks.OotTotal}";
                MmChecksText = $"[MM] {report.Checks.MmHave} / {report.Checks.MmTotal}";
                MultiworldText = _playerCount > 1 ? $"Multiworld: player {_playerId} of {_playerCount}" : "";
                Raise(nameof(ChecksText));
                Raise(nameof(ItemsOwnedText));
                Raise(nameof(OotChecksText));
                Raise(nameof(MmChecksText));
                Raise(nameof(MultiworldText));
                Raise(nameof(ShowMultiworldText));
                Goals.Clear();
                foreach (var goal in report.Goals)
                {
                    Goals.Add(new GoalRowViewModel(goal.Name,
                        goal.Active
                            ? $"{goal.Name}: {goal.Owned} / {goal.Threshold}{(goal.Satisfied ? "  ✓" : "")}"
                            : $"{goal.Name}: off",
                        goal.Active, goal.Satisfied));
                }

                Raise(nameof(HasGoals));
                break;
            case 1:
                Dungeons.Clear();
                foreach (var d in report.DungeonRows)
                {
                    Dungeons.Add(new DungeonRowViewModel(d));
                }

                break;
            case 2:
                SoulGroups.Clear();
                foreach (var sub in Enum.GetValues<SoulSub>())
                {
                    var list = report.Souls(game, sub);
                    SoulGroups.Add(new SoulGroupViewModel(
                        $"{sub} Souls  {list.Count(i => i.Owned)} / {list.Count}",
                        new ObservableCollection<ProgressItemRow>(
                            list.Select(i => new ProgressItemRow(i.Name, i.Owned)))));
                }

                break;
            default:
                var items = CategoryIndex switch
                {
                    3 => report.Songs(game),
                    4 => report.KeyItems(game),
                    _ => report.RustyKeys(game),
                };
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(new ProgressItemRow(item.Name, item.Owned));
                }

                ItemsHeader = items.Count == 0
                    ? CategoryIndex switch
                    {
                        3 => "(no songs in pool)",
                        4 => "(no major items)",
                        _ => "(rusty keys are not shuffled)",
                    }
                    : $"{items.Count(i => i.Owned)} / {items.Count}";
                Raise(nameof(ItemsHeader));
                break;
        }
    }

    public void LoadSampleData()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "preview",
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["BRIDGE"] = new(3, new Dictionary<string, bool> { ["stones"] = true }),
            },
        };
        var placements = new List<(ItemPlacement, bool)>
        {
            Row(Game.Oot, "C1", "OOT_SMALL_KEY_FOREST", true),
            Row(Game.Oot, "C2", "OOT_SMALL_KEY_FOREST", false),
            Row(Game.Oot, "C3", "OOT_MAP_FOREST", true),
            Row(Game.Oot, "C4", "OOT_STONE_EMERALD", true),
            Row(Game.Oot, "C5", "SHARED_SONG_EPONA", false),
            Row(Game.Mm, "C6", "MM_STRAY_FAIRY_WF", true),
            Row(Game.Mm, "C7", "MM_SOUL_BOSS_ODOLWA", false),
        };
        var counts = placements.Where(p => p.Item2)
            .GroupBy(p => p.Item1.Item.Id)
            .ToDictionary(g => g.Key, g => g.Count());
        SetReport(ProgressReport.Build(seed, placements, counts), 1, 1, "anywhere");

        static (ItemPlacement, bool) Row(Game game, string check, string item, bool collected) =>
            (new ItemPlacement(new CheckRef(game, check, check, [], null, "", null),
                               new ItemRef(item.StartsWith("MM_") ? Game.Mm : Game.Oot, item, item),
                               null, 1, 1, false), collected);
    }
}
