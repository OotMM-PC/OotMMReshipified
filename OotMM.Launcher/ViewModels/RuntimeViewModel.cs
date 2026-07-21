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
            () => coordinator.ResetSeedProgress());
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
        vm.RefreshFromCoordinator();
        vm.RecomputeLogic();
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

        if (changed)
        {
            RecomputeLogic();
            Tracker.NotifyCheckStatesChanged();
        }
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
