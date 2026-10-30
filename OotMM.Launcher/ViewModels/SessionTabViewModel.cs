using OotMM.Core;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

public sealed class SessionTabViewModel : ViewModelBase
{
    private readonly SessionCoordinator? _coordinator;
    private readonly Action? _resetProgress;
    private bool _devMode;
    private bool _resetArmed;
    private DateTime _resetArmedAt;

    public SessionTabViewModel()
    {
        RestartGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        SwitchGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        SwitchGameAsChildCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        SwitchGameAsAdultCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        EndSessionCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        ResetProgressCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        OpenGameMenuCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        ResetSpeedCommand = new RelayCommand(() => { });
    }

    public SessionTabViewModel(SessionCoordinator coordinator, Action end, Action resetProgress)
    {
        CoordinatorReady = true;
        _coordinator = coordinator;
        _resetProgress = resetProgress;
        RestartGameCommand = new RelayCommand(coordinator.RestartGame);
        SwitchGameCommand = new RelayCommand(() => SwitchTo(null));
        SwitchGameAsChildCommand = new RelayCommand(() => SwitchTo(1));
        SwitchGameAsAdultCommand = new RelayCommand(() => SwitchTo(0));
        EndSessionCommand = new RelayCommand(end);
        coordinator.StateChanged += RaiseSwitchVisibility;
        ResetProgressCommand = new RelayCommand(OnResetClicked);
        OpenGameMenuCommand = new RelayCommand(coordinator.OpenInGameMenu);
        ResetSpeedCommand = new RelayCommand(() => SpeedPercent = 100);
    }

    public bool CoordinatorReady { get; }

    public bool ShowUnavailableNote => !CoordinatorReady;

    public string UnavailableNote =>
        "Session controls come alive once a real session is running (start one from the config screen).";

    public RelayCommand RestartGameCommand { get; }
    public RelayCommand SwitchGameCommand { get; }
    public RelayCommand SwitchGameAsChildCommand { get; }
    public RelayCommand SwitchGameAsAdultCommand { get; }
    public RelayCommand EndSessionCommand { get; }

    private void SwitchTo(uint? ootAge)
    {
        _coordinator?.SwitchGame(ootAge);
        RaiseSwitchVisibility();
    }

    private void RaiseSwitchVisibility()
    {
        Raise(nameof(ShowSwitchToMm));
        Raise(nameof(ShowSwitchToOot));
    }
    public RelayCommand ResetProgressCommand { get; }
    public RelayCommand OpenGameMenuCommand { get; }
    public RelayCommand ResetSpeedCommand { get; }


    public bool ShowSpeed => _coordinator?.TimerEnabled ?? false;

    public double SpeedMin => _coordinator?.TimerMin ?? 0;

    public double SpeedMax => Math.Max(SpeedMin, _coordinator?.TimerMax ?? 250);

    public double SpeedPercent
    {
        get => _coordinator?.SpeedPercent ?? 100;
        set
        {
            if (_coordinator is not null && _coordinator.SpeedPercent != (int)value)
            {
                _coordinator.SpeedPercent = (int)value;
                Raise();
                Raise(nameof(SpeedText));
                Raise(nameof(ShowSpeedReset));
            }
        }
    }

    public string SpeedText => $"{(int)SpeedPercent}%";

    public bool ShowSpeedReset => (int)SpeedPercent != 100;

    public bool SpeedSmooth
    {
        get => _coordinator?.SpeedSmooth ?? false;
        set
        {
            if (_coordinator is not null)
            {
                _coordinator.SpeedSmooth = value;
                Raise();
            }
        }
    }

    public void NotifyRulesChanged()
    {
        Raise(nameof(ShowSpeed));
        Raise(nameof(SpeedMin));
        Raise(nameof(SpeedMax));
        Raise(nameof(SpeedPercent));
        Raise(nameof(SpeedText));
        Raise(nameof(ShowSpeedReset));
    }


    public bool DevMode
    {
        get => _devMode;
        set
        {
            if (Set(ref _devMode, value))
            {
                RaiseSwitchVisibility();
            }
        }
    }

    public bool ShowSwitchToMm => CoordinatorReady && DevMode && _coordinator?.ActiveGame == Game.Oot;

    public bool ShowSwitchToOot => CoordinatorReady && DevMode && _coordinator?.ActiveGame == Game.Mm;

    public bool ShowReset =>
        !CoordinatorReady || _coordinator?.Session.Seed.IsMultiworld != true;

    public bool ResetArmed
    {
        get => _resetArmed;
        private set
        {
            if (Set(ref _resetArmed, value))
            {
                Raise(nameof(ResetButtonText));
            }
        }
    }

    public string ResetButtonText => ResetArmed
        ? "Click again to DELETE all progress"
        : "Reset seed progress…";

    private void OnResetClicked()
    {
        if (ResetArmed && DateTime.UtcNow - _resetArmedAt <= TimeSpan.FromSeconds(5))
        {
            ResetArmed = false;
            _resetProgress?.Invoke();
            return;
        }

        ResetArmed = true;
        _resetArmedAt = DateTime.UtcNow;
    }

    public void DisarmReset() => ResetArmed = false;
}
