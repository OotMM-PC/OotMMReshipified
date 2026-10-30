namespace OotMM.Launcher.ViewModels;

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

    public bool CheatGatePending
    {
        get => _cheatGatePending;
        set => Set(ref _cheatGatePending, value);
    }

    public Action? RestartGame { get; set; }

    public void OnRestartClicked()
    {
        RestartGame?.Invoke();
        CheatGatePending = false;
    }

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
