namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Appearance options (lens of truth tab). Changes apply immediately through
/// callbacks owned by MainWindowViewModel, which also persists them.
/// </summary>
public sealed class OptionsTabViewModel : ViewModelBase
{
    private readonly Action<int> _onThemeChanged;
    private readonly Action<double> _onUiScaleChanged;
    private int _themeIndex;
    private double _uiScale;

    public OptionsTabViewModel(
        int themeIndex, double uiScale, Action<int> onThemeChanged, Action<double> onUiScaleChanged)
    {
        _themeIndex = themeIndex;
        _uiScale = uiScale;
        _onThemeChanged = onThemeChanged;
        _onUiScaleChanged = onUiScaleChanged;
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

    public double UiScale
    {
        get => _uiScale;
        set
        {
            if (Set(ref _uiScale, value))
            {
                _onUiScaleChanged(value);
                Raise(nameof(UiScalePercent));
            }
        }
    }

    public string UiScalePercent => $"{Math.Round(UiScale * 100)}%";
}
