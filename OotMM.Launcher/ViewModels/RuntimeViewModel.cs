using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

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
    private double _sidebarScale = 1.0;

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

        Tracker = new TrackerTabViewModel { DevMode = settings.DevMode };
        Log = new LogTabViewModel();
        SessionTab = sessionTab;
        SessionTab.DevMode = settings.DevMode;
        _sidebarScale = currentUiScale;
        Options = new OptionsTabViewModel(settings.Theme, currentUiScale, setTheme, scale =>
        {
            setUiScale(scale);
            SidebarScale = scale;
        }, settings.DevMode, ApplyDevMode);
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
            coordinator,
            () => coordinator.EndSession("Session ended by the player."),
            () => coordinator.ResetSeedProgress());
        var vm = new RuntimeViewModel(settings, false,
                                      () => coordinator.EndSession("Session ended by the player."),
                                      setTheme, setUiScale, currentUiScale, sessionTab)
        {
            _coordinator = coordinator,
            ActiveGame = coordinator.ActiveGame,
        };
        coordinator.AllowDebugMenus = settings.DevMode;
        vm.Tracker.ForceUnlockSender = coordinator.ForceUnlock;
        vm.Options.RestartGame = coordinator.RestartGame;

        var localPlayer = coordinator.Session.Seed.PlayerId;
        foreach (var placement in coordinator.Session.Seed.Placements)
        {
            var check = placement.Check;
            var row = new CheckRowViewModel(
                GameInfo.ToSeedTag(check.Game).ToUpperInvariant(),
                check.Name.Length > 0 ? check.Name : check.Id,
                placement.Item.Name,
                check.Id,
                placement.IsFor(localPlayer),
                vm.Tracker.OnRowClicked) { InLogic = true };
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

    public event Action? GameHostAttached;

    public void NotifyGameHostCreated(nint handle)
    {
        if (_coordinator is null)
        {
            return;
        }

        _coordinator.GameHostHandle = handle;
        GameHostAttached?.Invoke();
    }

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

    public double SidebarScale
    {
        get => _sidebarScale;
        private set
        {
            if (Set(ref _sidebarScale, value))
            {
                Raise(nameof(SidebarScaleTransform));
            }
        }
    }

    public Avalonia.Media.ScaleTransform SidebarScaleTransform => new(SidebarScale, SidebarScale);

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

        Tracker.UpdateStats(
            _placementRows.Count(pr => pr.Row.InLogic && !pr.Row.Collected),
            _solver is not null,
            _coordinator.Session.Seed.IsMultiworld);
        SessionTab.NotifyRulesChanged();
    }

    private void ApplyDevMode(bool devMode)
    {
        _settings.DevMode = devMode;
        _settings.Save();
        Tracker.DevMode = devMode;
        SessionTab.DevMode = devMode;
        if (_coordinator is not null)
        {
            _coordinator.AllowDebugMenus = devMode;
            Options.CheatGatePending = _coordinator.IsRunning;
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
            if (collected && placement.IsFor(seed.PlayerId))
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

        var report = ProgressReport.Build(seed, checks, counts,
                                          _coordinator.Multiworld?.ForeignOwnedChecks());
        Progress.SetReport(report, seed.PlayerId, seed.PlayerCount,
                           seed.GetStringSetting("songs", "anywhere"));
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
            input.Licenses[starting.Item.Id] = input.Licenses.GetValueOrDefault(starting.Item.Id) + starting.Count;
        }

        foreach (var (placement, row) in _placementRows)
        {
            if (!row.Collected || !placement.IsFor(seed.PlayerId))
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
