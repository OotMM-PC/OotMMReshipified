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
    private bool _relayStarted;

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
            }
        }
    }

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
            if (_settings.HostRelay)
            {
                StartHostedRelay(port);
            }

            multiworld = new MultiworldSync(session, host, port, _settings.PlayerName);
            multiworld.AutoHostWanted += () => StartHostedRelay(port);
        }

        _coordinator?.Dispose();
        _coordinator = new SessionCoordinator(session, multiworld);
        _coordinator.Ended += EndSessionToConfig;
        _coordinator.StateChanged += ApplyAccent;

        _runtime = RuntimeViewModel.CreateLive(_settings, _coordinator, SetTheme, SetUiScale, UiScale);

        // Boot only once the native game host exists, so the game embeds into it
        // from its first frame instead of opening a top-level window.
        var started = false;
        var coordinator = _coordinator;
        _runtime.GameHostAttached += () =>
        {
            if (started || coordinator != _coordinator)
            {
                return;
            }

            started = true;
            // The host handle arrives mid-layout; boot outside the layout pass.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (coordinator == _coordinator && coordinator.Start() is { } startError)
                {
                    EndSessionToConfig(startError);
                }
            });
        };

        if (multiworld is not null)
        {
            var (host, port) = ResolveRelayEndpoint();
            _runtime.Log.Add(DateTime.Now.ToString("HH:mm"),
                $"Multiworld: player {seed.PlayerId} → {host}:{port} (protocol v4); "
                + multiworld.ResumeSummary, isRemote: true);
            if (_relayStarted)
            {
                _runtime.Log.Add(DateTime.Now.ToString("HH:mm"),
                    $"Hosting the relay on port {port}.", isRemote: true);
            }
        }

        Config.IsActive = false;
        CurrentViewModel = _runtime;
    }

    /// <summary>Hosts the relay engine on a background thread; it outlives
    /// sessions, so re-hosting is a no-op once started.</summary>
    private void StartHostedRelay(ushort port)
    {
        if (_relayStarted)
        {
            return;
        }

        _relayStarted = true;
        var storeDir = Path.Combine(AppContext.BaseDirectory, "world_state");
        var rules = new OotMM.Core.Relay.RelayRules { Pvp = _settings.EnablePvp };
        var engine = new OotMM.Core.Relay.RelayEngine(storeDir, rules);
        new Thread(() => engine.Run(port, CancellationToken.None)) { IsBackground = true }.Start();
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

    public void OnWindowActivated() => _coordinator?.OnHostActivated();

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
