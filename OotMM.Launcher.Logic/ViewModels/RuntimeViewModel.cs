using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

public sealed class RuntimeViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private readonly Action _backToConfig;
    private readonly ViewModelBase[] _tabs;
    private readonly Dictionary<string, CheckRowViewModel> _checkRows = [];
    private readonly List<(ItemPlacement Placement, CheckRowViewModel Row)> _placementRows = [];
    private SessionCoordinator? _coordinator;
    private LogicSolver? _solver;
    private Game _activeGame;
    private bool _showSidebar;
    private int _selectedTabIndex;
    private bool _showMapTab;
    private bool _isRunning;
    private bool _isFinalizingSync;
    private long _seenReceiptRevision;
    private int _seenEndgameBosses;
    private string _multiworldStatus = "";
    private double _sidebarScale = 1.0;
    private int _seenEntranceRevision = -1;
    private MapPosition? _seenMapPosition;

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
        }, settings.DevMode, ApplyDevMode)
        {
            HoldToZTarget = settings.HoldToZTarget,
            HoldToZTargetChanged = ApplyHoldToZTarget,
            RemoteItemPrompts = settings.RemoteItemPrompts,
            RemoteItemPromptsChanged = ApplyRemoteItemPrompts,
            FairySpotPads = settings.FairySpotPads,
            FairySpotPadsChanged = ApplyFairySpotPads,
            PotRolling = settings.PotRolling,
            PotRollingChanged = ApplyPotRolling,
            ContentFilters = settings.ContentFilters,
            ContentFiltersChanged = ApplyContentFilters,
            EntranceMap = settings.EntranceMap,
            EntranceMapChanged = ApplyEntranceMap,
        };
        Tracker.ContentFiltersEnabled = settings.ContentFilters;
        Progress = new ProgressTabViewModel();
        Map = new MapTabViewModel();
        _tabs = [Tracker, Log, SessionTab, Options, Progress, Map];
        _showMapTab = settings.EntranceMap;

        _backToConfig = backToConfig;
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
        vm.Map.Attach(PreviewMap(settings.SeedPath));
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
        coordinator.ZTargetHold = settings.HoldToZTarget || coordinator.ZTargetHold;
        vm.Options.ShowHoldToZTarget(coordinator.ZTargetHold);
        coordinator.RemoteItemPrompts = settings.RemoteItemPrompts;
        coordinator.FairySpotPads = settings.FairySpotPads;
        coordinator.PotRolling = settings.PotRolling;
        coordinator.Traps = settings.Traps;
        vm.Tracker.ForceUnlockSender = coordinator.ForceUnlock;
        vm.Map.Attach(coordinator.EntranceMap);
        vm.Options.RestartGame = coordinator.RestartGame;

        var localPlayer = coordinator.Session.Seed.PlayerId;
        foreach (var placement in coordinator.Session.Seed.Placements)
        {
            var check = placement.Check;
            if (!coordinator.Session.Seed.IsTrackedPlacement(placement))
                continue;
            var row = new CheckRowViewModel(
                GameInfo.ToSeedTag(check.Game).ToUpperInvariant(),
                check.Name.Length > 0 ? check.Name : check.Id,
                placement.Item.Name,
                check.Id,
                placement.IsFor(localPlayer))
            {
                InLogic = true,
                Content = ContainerContents.Classify(coordinator.Session.Seed, placement),
                Required = placement.Required == true,
            };
            if (vm._checkRows.TryAdd(Identity.CheckKey(check.Game, check.Id), row))
            {
                vm._placementRows.Add((placement, row));
            }
        }

        vm.Tracker.SetChecks(vm._checkRows.Values,
                             coordinator.Session.Seed.Placements.Any(p => p.Required is not null));

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
        vm.UpdateContentAccess();
        vm.UpdateContentFilterRule();
        vm.UpdateEntranceMapRule();
        vm.Tracker.NotifyCheckStatesChanged();
        return vm;
    }

    public bool IsPreview { get; }

    public TrackerTabViewModel Tracker { get; }
    public LogTabViewModel Log { get; }
    public SessionTabViewModel SessionTab { get; }
    public OptionsTabViewModel Options { get; }
    public ProgressTabViewModel Progress { get; }
    public MapTabViewModel Map { get; }

    public void BackToConfig() => _backToConfig();

    private static EntranceMap? PreviewMap(string seedPath)
    {
        var pack = EntranceMapPack.LoadInstalled();
        if (pack is null || !File.Exists(seedPath))
        {
            return null;
        }

        try
        {
            return new EntranceMap(SeedLoader.LoadFromFile(seedPath), pack);
        }
        catch (SeedLoadException)
        {
            return null;
        }
    }

    public void ExpandSidebar() => ShowSidebar = true;

    public void CollapseSidebar() => ShowSidebar = false;

    public Game ActiveGame
    {
        get => _activeGame;
        private set
        {
            if (Set(ref _activeGame, value))
            {
                Raise(nameof(ActiveGameTitle));
                Raise(nameof(ActiveIsOot));
                Progress.FollowGame(value);
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

    internal event Action? GameHostAttached;

    public void SetLauncherKeyboard(bool wanted) => _coordinator?.SetLauncherKeyboard(wanted);

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
            var index = Math.Clamp(value, 0, _tabs.Length - 1);
            if (Set(ref _selectedTabIndex, _tabs[index] == Map && !_showMapTab ? 0 : index))
            {
                Raise(nameof(CurrentTab));
            }
        }
    }

    /// <summary>False while the player or the server has the entrance map turned off.</summary>
    public bool ShowMapTab
    {
        get => _showMapTab;
        private set
        {
            if (Set(ref _showMapTab, value) && !value && CurrentTab == Map)
            {
                SelectedTabIndex = 0;
            }
        }
    }

    public int MapTabIndex => Array.IndexOf(_tabs, Map);

    public ViewModelBase CurrentTab => _tabs[_selectedTabIndex];

    public double SidebarScale
    {
        get => _sidebarScale;
        private set => Set(ref _sidebarScale, value);
    }

    private void RefreshFromCoordinator()
    {
        if (_coordinator is null)
        {
            return;
        }

        ActiveGame = _coordinator.ActiveGame;
        IsRunning = _coordinator.IsRunning;
        UpdateMultiworldStatus();
        UpdateContentFilterRule();
        UpdateEntranceMapRule();
        if (_coordinator.EntranceRevision != _seenEntranceRevision || _coordinator.MapPosition != _seenMapPosition)
        {
            _seenEntranceRevision = _coordinator.EntranceRevision;
            _seenMapPosition = _coordinator.MapPosition;
            Map.Update(_seenMapPosition);
        }

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
            UpdateContentAccess();
            Tracker.NotifyCheckStatesChanged();
        }
        else if (_coordinator.EndgameBosses.Count != _seenEndgameBosses)
        {
            RebuildProgress();
        }

        Tracker.UpdateStats(
            _placementRows.Count(pr => pr.Row.InLogic && !pr.Row.Collected),
            _solver is not null,
            _coordinator.Session.Seed.IsMultiworld);
        SessionTab.NotifyRulesChanged();
    }

    private void ApplyHoldToZTarget(bool hold)
    {
        _settings.HoldToZTarget = hold;
        _settings.Save();
        if (_coordinator is not null)
        {
            _coordinator.ZTargetHold = hold;
        }
    }

    private void ApplyRemoteItemPrompts(bool prompts)
    {
        _settings.RemoteItemPrompts = prompts;
        _settings.Save();
        if (_coordinator is not null)
        {
            _coordinator.RemoteItemPrompts = prompts;
        }
    }

    private void ApplyFairySpotPads(bool pads)
    {
        _settings.FairySpotPads = pads;
        _settings.Save();
        if (_coordinator is not null)
        {
            _coordinator.FairySpotPads = pads;
        }
    }

    private void ApplyPotRolling(bool rolling)
    {
        _settings.PotRolling = rolling;
        _settings.Save();
        if (_coordinator is not null)
        {
            _coordinator.PotRolling = rolling;
        }
    }

    private void ApplyContentFilters(bool filters)
    {
        _settings.ContentFilters = filters;
        _settings.Save();
        UpdateContentFilterRule();
    }

    private void UpdateContentFilterRule()
    {
        Options.ContentFiltersAvailable =
            _coordinator?.Session.Seed.GetStringSetting("csmc", "always") != "never";
        Options.ContentFiltersNote = HostRuleNote(_coordinator?.Multiworld?.ContentFilters);
        Tracker.ContentFiltersEnabled = _settings.ContentFilters && Options.ContentFiltersAvailable
                                        && Options.ContentFiltersAllowed;
    }

    private void ApplyEntranceMap(bool map)
    {
        _settings.EntranceMap = map;
        _settings.Save();
        UpdateEntranceMapRule();
    }

    private void UpdateEntranceMapRule()
    {
        Options.EntranceMapNote = HostRuleNote(_coordinator?.Multiworld?.EntranceMap);
        ShowMapTab = _settings.EntranceMap && Options.EntranceMapAllowed;
    }

    private string HostRuleNote(bool? allowed) =>
        _coordinator?.Multiworld is null || allowed == true ? ""
        : allowed == false ? "The server has turned this off for this session."
        : "Available once the server's rules arrive.";

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
        var counts = OwnedItemCounts();

        _seenEndgameBosses = _coordinator.EndgameBosses.Count;
        var report = ProgressReport.Build(seed, checks, counts,
                                          _coordinator.Multiworld?.ForeignOwnedChecks());
        Progress.SetReport(report, seed.GetStringSetting("goal", "both"), seed.PlayerCount > 1,
                           _coordinator.EndgameBosses.Contains(Endgame.Ganon),
                           _coordinator.EndgameBosses.Contains(Endgame.Majora));
    }

    private Dictionary<string, int> OwnedItemCounts()
    {
        var seed = _coordinator!.Session.Seed;
        var counts = new Dictionary<string, int>();
        foreach (var starting in seed.StartingItems)
        {
            counts[starting.Item.Id] = counts.GetValueOrDefault(starting.Item.Id) + starting.Count;
        }

        foreach (var (placement, row) in _placementRows)
        {
            if (row.Collected && placement.IsFor(seed.PlayerId))
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

        return counts;
    }

    private void UpdateContentAccess()
    {
        if (_coordinator is null)
        {
            return;
        }

        var seed = _coordinator.Session.Seed;
        var counts = OwnedItemCounts();
        bool Owns(string itemId) => counts.GetValueOrDefault(itemId) > 0;
        Tracker.SetContentAccess(
            ContainerContents.IsRevealed(seed, Game.Oot, Owns),
            ContainerContents.IsRevealed(seed, Game.Mm, Owns),
            seed.GetBoolSetting("stoneAgonyMm") && !seed.GetBoolSetting("sharedStoneAgony"));
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
            if (row.Collected && placement.IsFor(seed.PlayerId))
            {
                AddLogicItem(input, placement.Item.Id, placement.Check.Name);
            }
        }

        if (_coordinator.Multiworld is { } sync)
        {
            foreach (var placement in sync.RemoteItemPlacements)
            {
                var location = seed.FindPlacement(placement.Check.Game, placement.Check.Id)?.Check.Name
                               ?? placement.Check.Name;
                AddLogicItem(input, placement.Item.Id, location);
            }
        }

        var reachable = _solver.Solve(input);
        foreach (var (placement, row) in _placementRows)
        {
            row.InLogic = reachable.Contains(placement.Check.Name);
        }
    }

    private void AddLogicItem(LogicSolver.Input input, string itemId, string location)
    {
        input.Items[itemId] = input.Items.GetValueOrDefault(itemId) + 1;
        if (_solver!.IsRenewableLocation(location))
        {
            input.Renewables[itemId] = input.Renewables.GetValueOrDefault(itemId) + 1;
        }

        if (_solver.IsLicenseLocation(location))
        {
            input.Licenses[itemId] = input.Licenses.GetValueOrDefault(itemId) + 1;
        }
    }

    private void RaiseSidebarStates()
    {
        Raise(nameof(ShowFullSidebar));
        Raise(nameof(ShowCollapsedStrip));
    }
}
