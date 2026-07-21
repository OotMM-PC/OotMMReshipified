namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Appearance options (lens of truth tab). Changes apply immediately through
/// callbacks owned by MainWindowViewModel, which also persists them.
/// </summary>
public sealed class OptionsTabViewModel : ViewModelBase
{
    private readonly Action<int> _onThemeChanged;
    private readonly Action<double> _onUiScaleChanged;
    private readonly Action<bool>? _onDevModeChanged;
    private int _themeIndex;
    private double _uiScale;
    private bool _devMode;
    private bool _cheatGatePending;

    public OptionsTabViewModel(
        int themeIndex, double uiScale, Action<int> onThemeChanged, Action<double> onUiScaleChanged,
        bool devMode = false, Action<bool>? onDevModeChanged = null)
    {
        _themeIndex = themeIndex;
        _uiScale = uiScale;
        _devMode = devMode;
        _onThemeChanged = onThemeChanged;
        _onUiScaleChanged = onUiScaleChanged;
        _onDevModeChanged = onDevModeChanged;
    }

    /// <summary>Dev mode: unlocks the games' debug/cheat menus (next boot) plus
    /// the launcher's force-unlock cheat and manual game switching.</summary>
    public bool DevMode
    {
        get => _devMode;
        set
        {
            if (Set(ref _devMode, value))
            {
                _onDevModeChanged?.Invoke(value);
            }
        }
    }

    /// <summary>Set by the owner while a game is running; the games read the
    /// cheat gate at boot, so the change needs a restart to show up.</summary>
    public bool CheatGatePending
    {
        get => _cheatGatePending;
        set => Set(ref _cheatGatePending, value);
    }

    /// <summary>Assigned by the owner in live sessions (restarts the active game).</summary>
    public Action? RestartGame { get; set; }

    public void OnRestartClicked()
    {
        RestartGame?.Invoke();
        CheatGatePending = false;
    }

    /// <summary>0 = follow the active game, 1 = OoT gold, 2 = MM violet —
    /// same three modes as the original theme setting.</summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (Set(ref _themeIndex, value))
            {
                _onThemeChanged(value);
            }
        }
    }

    /// <summary>Live drag value; the window rescales only on CommitUiScale so the
    /// slider never moves under a captured pointer.</summary>
    public double UiScale
    {
        get => _uiScale;
        set
        {
            if (Set(ref _uiScale, value))
            {
                Raise(nameof(UiScalePercent));
            }
        }
    }

    public void CommitUiScale() => _onUiScaleChanged(_uiScale);

    public string UiScalePercent => $"{Math.Round(UiScale * 100)}%";
}
