using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

/// <summary>The in-session screen: game area plus the sidebar tabs. Runs in
/// preview mode (sample data) or live mode (driven by a SessionCoordinator).</summary>
public sealed class RuntimeViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private readonly ViewModelBase[] _tabs;
    private readonly Dictionary<string, CheckRowViewModel> _checkRows = [];
    private readonly List<(ItemPlacement Placement, CheckRowViewModel Row)> _placementRows = [];
    private SessionCoordinator? _coordinator;
    private LogicSolver? _solver;
    private Game _activeGame;
    private bool _showSidebar;
    private int _selectedTabIndex;
    private bool _isRunning;
    private bool _isFinalizingSync;
    private long _seenReceiptRevision;
    private string _multiworldStatus = "";

    private RuntimeViewModel(
        UiSettings settings,
        bool isPreview,
        Action backToConfig,
        Action<int> setTheme,
        Action<double> setUiScale,
        double currentUiScale,
        SessionTabViewModel sessionTab)
    {
        _settings = settings;
        IsPreview = isPreview;
        _showSidebar = settings.ShowSidebar;

        Tracker = new TrackerTabViewModel();
        Log = new LogTabViewModel();
        SessionTab = sessionTab;
        Options = new OptionsTabViewModel(settings.Theme, currentUiScale, setTheme, setUiScale);
        Progress = new ProgressTabViewModel();
        _tabs = [Tracker, Log, SessionTab, Options, Progress];

        BackToConfigCommand = new RelayCommand(backToConfig);
        ExpandSidebarCommand = new RelayCommand(() => ShowSidebar = true);
        CollapseSidebarCommand = new RelayCommand(() => ShowSidebar = false);
    }

    public static RuntimeViewModel CreatePreview(
        UiSettings settings,
        Action backToConfig,
        Action<int> setTheme,
        Action<double> setUiScale,
        double currentUiScale)
    {
        var vm = new RuntimeViewModel(settings, true, backToConfig, setTheme, setUiScale,
                                      currentUiScale, new SessionTabViewModel());
        vm.Tracker.LoadSampleData();
        vm.Log.LoadSampleData();
        vm.Progress.LoadSampleData();
        return vm;
    }

    public static RuntimeViewModel CreateLive(
        UiSettings settings,
        SessionCoordinator coordinator,
        Action<int> setTheme,
        Action<double> setUiScale,
        double currentUiScale)
    {
        var sessionTab = new SessionTabViewModel(
            coordinator.RestartGame,
            coordinator.SwitchGame,
            () => coordinator.EndSession("Session ended by the player."),
            () => coordinator.ResetSeedProgress(),
            coordinator.OpenInGameMenu);
        var vm = new RuntimeViewModel(settings, false,
                                      () => coordinator.EndSession("Session ended by the player."),
                                      setTheme, setUiScale, currentUiScale, sessionTab)
        {
            _coordinator = coordinator,
            ActiveGame = coordinator.ActiveGame,
        };

        foreach (var placement in coordinator.Session.Seed.Placements)
        {
            var check = placement.Check;
            var row = new CheckRowViewModel(
                GameInfo.ToSeedTag(check.Game).ToUpperInvariant(),
                check.Name.Length > 0 ? check.Name : check.Id,
                placement.Item.Name) { InLogic = true };
            if (vm._checkRows.TryAdd(Identity.CheckKey(check.Game, check.Id), row))
            {
                vm._placementRows.Add((placement, row));
            }
        }

        vm.Tracker.SetChecks(vm._checkRows.Values);

        var solver = new LogicSolver();
        if (solver.LoadFromSeedFile(coordinator.Session.SeedPath))
        {
            vm._solver = solver;
        }

        coordinator.StateChanged += vm.RefreshFromCoordinator;
        coordinator.EventLogged += message => vm.Log.Add(DateTime.Now.ToString("HH:mm"), message);
        coordinator.RemoteEventLogged += message =>
            vm.Log.Add(DateTime.Now.ToString("HH:mm"), message, isRemote: true);
        vm.RefreshFromCoordinator();
        vm.RecomputeLogic();
        vm.RebuildProgress();
        vm.Tracker.NotifyCheckStatesChanged();
        return vm;
    }

    public bool IsPreview { get; }

    public TrackerTabViewModel Tracker { get; }
    public LogTabViewModel Log { get; }
    public SessionTabViewModel SessionTab { get; }
    public OptionsTabViewModel Options { get; }
    public ProgressTabViewModel Progress { get; }

    public RelayCommand BackToConfigCommand { get; }
    public RelayCommand ExpandSidebarCommand { get; }
    public RelayCommand CollapseSidebarCommand { get; }

    public Game ActiveGame
    {
        get => _activeGame;
        private set
        {
            if (Set(ref _activeGame, value))
            {
                Raise(nameof(ActiveGameTitle));
                Raise(nameof(ActiveIsOot));
            }
        }
    }

    public string ActiveGameTitle => GameInfo.Title(ActiveGame);

    public bool ActiveIsOot => ActiveGame == Game.Oot;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(RunStatusText));
            }
        }
    }

    public string RunStatusText => IsRunning ? "running" : "not running";

    /// <summary>Fires once the native game host exists; the owner boots the game
    /// then so it embeds from its first frame.</summary>
    public event Action? GameHostAttached;

    public void NotifyGameHostCreated(nint handle, Avalonia.PixelSize size)
    {
        if (_coordinator is null)
        {
            return;
        }

        _coordinator.GameHostHandle = handle;
        _coordinator.GameHostSize = (size.Width, size.Height);
        GameHostAttached?.Invoke();
    }

    public void NotifyGameHostResized(Avalonia.PixelSize size)
    {
        if (_coordinator is not null)
        {
            _coordinator.GameHostSize = (size.Width, size.Height);
        }
    }

    /// <summary>End-of-session drain: while pending durable writes flush, the
    /// sidebar exposes no relaunch/reset controls (a new child could be orphaned).</summary>
    public bool IsFinalizingSync
    {
        get => _isFinalizingSync;
        set
        {
            if (Set(ref _isFinalizingSync, value))
            {
                RaiseSidebarStates();
            }
        }
    }

    public bool ShowSidebar
    {
        get => _showSidebar;
        set
        {
            if (Set(ref _showSidebar, value))
            {
                _settings.ShowSidebar = value;
                _settings.Save();
                RaiseSidebarStates();
            }
        }
    }

    public bool ShowFullSidebar => ShowSidebar && !IsFinalizingSync;

    public bool ShowCollapsedStrip => !ShowSidebar && !IsFinalizingSync;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (Set(ref _selectedTabIndex, Math.Clamp(value, 0, _tabs.Length - 1)))
            {
                Raise(nameof(CurrentTab));
            }
        }
    }

    public ViewModelBase CurrentTab => _tabs[_selectedTabIndex];

    private void RefreshFromCoordinator()
    {
        if (_coordinator is null)
        {
            return;
        }

        ActiveGame = _coordinator.ActiveGame;
        IsRunning = _coordinator.IsRunning;
        UpdateMultiworldStatus();

        var changed = false;
        foreach (var (key, row) in _checkRows)
        {
            var collected = _coordinator.CompletedChecks.Contains(key);
            if (row.Collected != collected)
            {
                row.Collected = collected;
                changed = true;
            }
        }

        // Remote receipts change item counts without touching any check row.
        if (_coordinator.Multiworld is { } sync && sync.ReceiptRevision != _seenReceiptRevision)
        {
            _seenReceiptRevision = sync.ReceiptRevision;
            changed = true;
        }

        if (changed)
        {
            RecomputeLogic();
            RebuildProgress();
            Tracker.NotifyCheckStatesChanged();
        }
    }

    public string MultiworldStatus
    {
        get => _multiworldStatus;
        private set
        {
            if (Set(ref _multiworldStatus, value))
            {
                Raise(nameof(ShowMultiworldStatus));
            }
        }
    }

    public bool ShowMultiworldStatus => MultiworldStatus.Length > 0;

    private void UpdateMultiworldStatus()
    {
        if (_coordinator?.Multiworld is not { } sync)
        {
            return;
        }

        MultiworldStatus = sync.State switch
        {
            TransportState.Connecting => "Relay: connecting…",
            TransportState.Offline => $"Relay: offline — {sync.PendingChecks} check(s) waiting",
            TransportState.Syncing => $"Relay: syncing ({sync.PendingChecks} pending)",
            TransportState.Healthy => "Relay: in sync",
            TransportState.LocalError => "Relay: local write error — retrying",
            _ => "Relay: protocol error",
        };
    }

    private void RebuildProgress()
    {
        if (_coordinator is null)
        {
            return;
        }

        var seed = _coordinator.Session.Seed;
        var checks = _placementRows
            .Select(pr => (pr.Placement, pr.Row.Collected))
            .ToList();
        var counts = new Dictionary<string, int>();
        foreach (var starting in seed.StartingItems)
        {
            counts[starting.Item.Id] = counts.GetValueOrDefault(starting.Item.Id) + starting.Count;
        }

        foreach (var (placement, collected) in checks)
        {
            if (collected && placement.OwnerPlayer == seed.PlayerId)
            {
                counts[placement.Item.Id] = counts.GetValueOrDefault(placement.Item.Id) + 1;
            }
        }

        if (_coordinator.Multiworld is { } sync)
        {
            foreach (var (itemId, count) in sync.RemoteItemCounts)
            {
                counts[itemId] = counts.GetValueOrDefault(itemId) + count;
            }
        }

        var report = ProgressReport.Build(seed, checks, counts);
        var sections = new List<ProgressSectionViewModel>();

        var checksSection = new ProgressSectionViewModel("Checks");
        checksSection.Items.Add(new ProgressItemViewModel("All", report.Checks.Have, report.Checks.Total));
        checksSection.Items.Add(new ProgressItemViewModel("Ocarina of Time", report.Checks.OotHave, report.Checks.OotTotal));
        checksSection.Items.Add(new ProgressItemViewModel("Majora's Mask", report.Checks.MmHave, report.Checks.MmTotal));
        sections.Add(checksSection);

        if (report.Goals.Any(g => g.Active))
        {
            var goals = new ProgressSectionViewModel("Goals");
            foreach (var goal in report.Goals.Where(g => g.Active))
            {
                goals.Items.Add(new ProgressItemViewModel(goal.Name, goal.Owned, goal.Threshold));
            }

            sections.Add(goals);
        }

        if (report.DungeonRows.Count > 0)
        {
            var dungeons = new ProgressSectionViewModel("Dungeons");
            foreach (var d in report.DungeonRows)
            {
                var parts = new List<string>();
                if (d.HasKeyRing)
                {
                    parts.Add(d.KeyRing ? "key ring ✓" : "key ring ✗");
                }

                if (d.SkeletonOwned)
                {
                    parts.Add("skeleton key");
                }

                if (d.HasMap)
                {
                    parts.Add(d.Map ? "map ✓" : "map ✗");
                }

                if (d.HasCompass)
                {
                    parts.Add(d.Compass ? "compass ✓" : "compass ✗");
                }

                if (d.HasBossKey)
                {
                    parts.Add(d.BossKey ? "boss key ✓" : "boss key ✗");
                }

                if (d.HasFairies)
                {
                    parts.Add($"fairies {d.FairiesHave}/{d.FairiesTotal}");
                }

                dungeons.Items.Add(new ProgressItemViewModel(
                    d.Name, d.KeysHave, d.KeysTotal, string.Join(" · ", parts)));
            }

            sections.Add(dungeons);
        }

        foreach (var game in new[] { Game.Oot, Game.Mm })
        {
            var title = GameInfo.Title(game);
            AddItemSection(sections, $"{title} Songs", report.Songs(game));
            AddItemSection(sections, $"{title} Key Items", report.KeyItems(game));
            AddItemSection(sections, $"{title} Rusty Keys", report.RustyKeys(game));

            var souls = new ProgressSectionViewModel($"{title} Souls");
            foreach (var sub in Enum.GetValues<SoulSub>())
            {
                var list = report.Souls(game, sub);
                if (list.Count > 0)
                {
                    souls.Items.Add(new ProgressItemViewModel(
                        $"{sub} Souls", list.Count(i => i.Owned), list.Count));
                }
            }

            if (souls.Items.Count > 0)
            {
                sections.Add(souls);
            }
        }

        Progress.SetSections(sections);
    }

    private static void AddItemSection(
        List<ProgressSectionViewModel> sections, string title, IReadOnlyList<ItemProgress> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var section = new ProgressSectionViewModel(title);
        foreach (var item in items)
        {
            section.Items.Add(new ProgressItemViewModel(item.Name, item.Have, item.Total));
        }

        sections.Add(section);
    }

    private void RecomputeLogic()
    {
        if (_solver is null || _coordinator is null)
        {
            return;
        }

        var seed = _coordinator.Session.Seed;
        var input = new LogicSolver.Input();
        foreach (var starting in seed.StartingItems)
        {
            input.Items[starting.Item.Id] = input.Items.GetValueOrDefault(starting.Item.Id) + starting.Count;
            // The generator seeds license counts (but not renewables) from starting items.
            input.Licenses[starting.Item.Id] = input.Licenses.GetValueOrDefault(starting.Item.Id) + starting.Count;
        }

        foreach (var (placement, row) in _placementRows)
        {
            if (!row.Collected || placement.OwnerPlayer != seed.PlayerId)
            {
                continue;
            }

            var itemId = placement.Item.Id;
            input.Items[itemId] = input.Items.GetValueOrDefault(itemId) + 1;
            if (_solver.IsRenewableLocation(placement.Check.Name))
            {
                input.Renewables[itemId] = input.Renewables.GetValueOrDefault(itemId) + 1;
            }

            if (_solver.IsLicenseLocation(placement.Check.Name))
            {
                input.Licenses[itemId] = input.Licenses.GetValueOrDefault(itemId) + 1;
            }
        }

        // Multiworld receipts count as plain items (no renewable/license credit).
        if (_coordinator.Multiworld is { } sync)
        {
            foreach (var (itemId, count) in sync.RemoteItemCounts)
            {
                input.Items[itemId] = input.Items.GetValueOrDefault(itemId) + count;
            }
        }

        var reachable = _solver.Solve(input);
        foreach (var (placement, row) in _placementRows)
        {
            row.InLogic = reachable.Contains(placement.Check.Name);
        }
    }

    private void RaiseSidebarStates()
    {
        Raise(nameof(ShowFullSidebar));
        Raise(nameof(ShowCollapsedStrip));
    }
}
