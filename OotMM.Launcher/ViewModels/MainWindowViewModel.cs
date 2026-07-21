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

        MultiworldSync? multiworld = null;
        if (seed.IsMultiworld)
        {
            var (host, port) = ResolveRelayEndpoint();
            multiworld = new MultiworldSync(session, host, port, _settings.PlayerName);
        }

        _coordinator?.Dispose();
        _coordinator = new SessionCoordinator(session, multiworld);
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

        if (multiworld is not null)
        {
            var (host, port) = ResolveRelayEndpoint();
            _runtime.Log.Add(DateTime.Now.ToString("HH:mm"),
                $"Multiworld: player {seed.PlayerId} → {host}:{port} (protocol v4); "
                + multiworld.ResumeSummary, isRemote: true);
            if (_settings.HostRelay)
            {
                _runtime.Log.Add(DateTime.Now.ToString("HH:mm"),
                    "Relay hosting arrives with the server module — run ootmm_server on this machine meanwhile.",
                    isRemote: true);
            }
        }

        Config.IsActive = false;
        CurrentViewModel = _runtime;
    }

    private (string Host, ushort Port) ResolveRelayEndpoint()
    {
        if (_settings.HostRelay)
        {
            return ("127.0.0.1", (ushort)Math.Clamp(_settings.RelayPort, 1, 65535));
        }

        var address = _settings.MultiworldAddress.Trim();
        var colon = address.LastIndexOf(':');
        if (colon > 0 && ushort.TryParse(address[(colon + 1)..], out var port) && port > 0)
        {
            return (address[..colon], port);
        }

        return (address.Length > 0 ? address : "127.0.0.1", RelayProtocol.DefaultPort);
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
