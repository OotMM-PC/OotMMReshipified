using Avalonia.Media;
using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

/// <summary>Owns the two top-level stages (config form, runtime screen), the
/// live session coordinator, and app-wide appearance (accent, UI scale).</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private ViewModelBase _currentViewModel;
    private RuntimeViewModel? _runtime;
    private SessionCoordinator? _coordinator;
    private double _uiScale;

    public MainWindowViewModel(UiSettings settings, IFilePicker picker)
    {
        _settings = settings;
        _uiScale = Math.Clamp(settings.UiScale, 0.75, 1.5);
        Config = new ConfigViewModel(settings, picker, ShowRuntimePreview, StartSession);
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

    private void StartSession(Seed seed, string seedPath, string ootExe, string mmExe)
    {
        var session = new Core.Session
        {
            SeedPath = seedPath,
            OotExe = ootExe,
            MmExe = mmExe,
            Seed = seed,
            V4Planned = seed.IsMultiworld,
        };

        if (SessionPaths.PrepareSession(session) is { } prepareError)
        {
            Config.ShowStatus(prepareError);
            return;
        }

        if (seed.IsMultiworld)
        {
            DurableFile.WriteAtomic(Path.Combine(session.SessionDir, "multiworld.v4"), "4\n");
            // Never clobber a sidecar with real coverage from an earlier relay
            // session; without a relay client we could only write an empty one.
            var sidecarPath = Path.Combine(session.SessionDir, "foreignplacements.v4.json");
            if (!File.Exists(sidecarPath))
            {
                DurableFile.WriteAtomic(sidecarPath,
                    V4Transport.BuildForeignPlacementsSidecar(seed, new Dictionary<ushort, Seed>()));
            }
        }

        _coordinator?.Dispose();
        _coordinator = new SessionCoordinator(session);
        _coordinator.Ended += EndSessionToConfig;
        _coordinator.StateChanged += ApplyAccent;

        _runtime = RuntimeViewModel.CreateLive(_settings, _coordinator, SetTheme, SetUiScale, UiScale);
        if (_coordinator.Start() is { } startError)
        {
            _coordinator.Dispose();
            _coordinator = null;
            _runtime = null;
            Config.ShowStatus(startError);
            return;
        }

        if (seed.IsMultiworld)
        {
            _runtime.Log.Add(DateTime.Now.ToString("HH:mm"),
                "Relay not connected — multiworld sync arrives with the RelayClient module.", isRemote: true);
        }

        Config.IsActive = false;
        CurrentViewModel = _runtime;
    }

    private void EndSessionToConfig(string reason)
    {
        _coordinator?.Dispose();
        _coordinator = null;
        _runtime = null;
        Config.IsActive = true;
        Config.ShowStatus(reason);
        CurrentViewModel = Config;
    }

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

    private void ApplyAccent()
    {
        var violet = _settings.Theme == 2
                     || (_settings.Theme == 0
                         && CurrentViewModel is RuntimeViewModel runtime
                         && runtime.ActiveGame == Game.Mm);
        App.ApplyAccent(violet);
    }
}
