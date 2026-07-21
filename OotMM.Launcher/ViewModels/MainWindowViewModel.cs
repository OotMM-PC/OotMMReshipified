using Avalonia.Media;
using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Owns the two top-level stages (config form, runtime screen) and the
/// app-wide appearance settings (accent, UI scale). Mirrors the old launcher's
/// Stage enum: exactly one stage is visible at a time and the accent follows
/// the stage unless the user pinned a theme.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private ViewModelBase _currentViewModel;
    private RuntimeViewModel? _runtime;
    private double _uiScale;

    public MainWindowViewModel(UiSettings settings, IFilePicker picker)
    {
        _settings = settings;
        _uiScale = Math.Clamp(settings.UiScale, 0.75, 1.5);
        Config = new ConfigViewModel(settings, picker, ShowRuntimePreview);
        _currentViewModel = Config;
        ApplyAccent();
    }

    public ConfigViewModel Config { get; }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (Set(ref _currentViewModel, value))
            {
                ApplyAccent();
            }
        }
    }

    /// <summary>Whole-window zoom, applied via a LayoutTransform on the window
    /// content — the equivalent of the old launcher's uiScale.</summary>
    public double UiScale
    {
        get => _uiScale;
        set
        {
            var clamped = Math.Clamp(value, 0.75, 1.5);
            if (Set(ref _uiScale, clamped))
            {
                _settings.UiScale = clamped;
                _settings.Save();
                Raise(nameof(UiScaleTransform));
            }
        }
    }

    public ScaleTransform UiScaleTransform => new(UiScale, UiScale);

    /// <summary>Runtime layout preview: shows the session screen with sample
    /// data so the sidebar can be designed and exercised before the process
    /// coordinator exists. Entered from a link on the config screen.</summary>
    private void ShowRuntimePreview()
    {
        _runtime = RuntimeViewModel.CreatePreview(_settings, BackToConfig, SetTheme, SetUiScale, UiScale);
        Config.IsActive = false;
        CurrentViewModel = _runtime;
    }

    private void BackToConfig()
    {
        _runtime = null;
        Config.IsActive = true;
        CurrentViewModel = Config;
    }

    private void SetTheme(int theme)
    {
        _settings.Theme = theme;
        _settings.Save();
        ApplyAccent();
    }

    private void SetUiScale(double scale) => UiScale = scale;

    /// <summary>Ported CurrentAccent: explicit theme wins; otherwise gold on the
    /// config screen and the active game's color at runtime.</summary>
    private void ApplyAccent()
    {
        var violet = _settings.Theme == 2
                     || (_settings.Theme == 0
                         && CurrentViewModel is RuntimeViewModel runtime
                         && runtime.ActiveGame == Game.Mm);
        App.ApplyAccent(violet);
    }
}
