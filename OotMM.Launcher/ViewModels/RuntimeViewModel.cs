using OotMM.Core;
using OotMM.Launcher.Models;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// The in-session screen: the embedded game window on the left, the sidebar
/// (tracker / log / session / options / progress) on the right. Until the
/// process coordinator is ported this screen runs in preview mode with sample
/// data; every property here is the seam the coordinator will drive later.
/// </summary>
public sealed class RuntimeViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private readonly ViewModelBase[] _tabs;
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
        double currentUiScale)
    {
        _settings = settings;
        IsPreview = isPreview;
        _showSidebar = settings.ShowSidebar;

        Tracker = new TrackerTabViewModel();
        Log = new LogTabViewModel();
        Session = new SessionTabViewModel();
        Options = new OptionsTabViewModel(settings.Theme, currentUiScale, setTheme, setUiScale);
        Progress = new ProgressTabViewModel();

        // Rail order mirrors the original icon rail:
        // dungeon map, bombers notebook, kokiri sword, lens of truth, compass.
        _tabs = [Tracker, Log, Session, Options, Progress];

        BackToConfigCommand = new RelayCommand(backToConfig);
        ExpandSidebarCommand = new RelayCommand(() => ShowSidebar = true);
        CollapseSidebarCommand = new RelayCommand(() => ShowSidebar = false);
    }

    /// <summary>Builds the screen with representative sample data — reachable
    /// from the config screen so the layout can be exercised without a session.</summary>
    public static RuntimeViewModel CreatePreview(
        UiSettings settings,
        Action backToConfig,
        Action<int> setTheme,
        Action<double> setUiScale,
        double currentUiScale)
    {
        var vm = new RuntimeViewModel(settings, true, backToConfig, setTheme, setUiScale, currentUiScale)
        {
            ActiveGame = Game.Oot,
        };
        vm.Tracker.LoadSampleData();
        vm.Log.LoadSampleData();
        vm.Progress.LoadSampleData();
        return vm;
    }

    public bool IsPreview { get; }

    public TrackerTabViewModel Tracker { get; }
    public LogTabViewModel Log { get; }
    public SessionTabViewModel Session { get; }
    public OptionsTabViewModel Options { get; }
    public ProgressTabViewModel Progress { get; }

    public RelayCommand BackToConfigCommand { get; }
    public RelayCommand ExpandSidebarCommand { get; }
    public RelayCommand CollapseSidebarCommand { get; }

    public Game ActiveGame { get; private set; }

    public string ActiveGameTitle => GameInfo.Title(ActiveGame);

    /// <summary>Enum comparisons are awkward in bindings; the views pick the
    /// game emblem and accent-sensitive bits off this flag.</summary>
    public bool ActiveIsOot => ActiveGame == Game.Oot;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(RunStatusText));
            }
        }
    }

    public string RunStatusText => IsRunning ? "running" : "not running";

    /// <summary>End-of-session drain: while the last durable inbox writes are
    /// pending, the sidebar must expose no relaunch/reset controls (starting a
    /// new child during the drain would orphan the process) — so the view swaps
    /// to a wait message when this is set.</summary>
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

    /// <summary>The three mutually exclusive sidebar states the view renders:
    /// finalizing-drain wait screen, collapsed strip, or full panel.</summary>
    public bool ShowFullSidebar => ShowSidebar && !IsFinalizingSync;

    public bool ShowCollapsedStrip => !ShowSidebar && !IsFinalizingSync;

    private void RaiseSidebarStates()
    {
        Raise(nameof(ShowFullSidebar));
        Raise(nameof(ShowCollapsedStrip));
    }

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
}
