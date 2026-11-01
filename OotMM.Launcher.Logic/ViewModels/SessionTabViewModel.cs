using OotMM.Core;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

public sealed class SessionTabViewModel : ViewModelBase
{
    private readonly SessionCoordinator? _coordinator;
    private readonly Action? _end;
    private readonly Action? _resetProgress;
    private bool _devMode;
    private bool _resetArmed;
    private DateTime _resetArmedAt;

    public SessionTabViewModel()
    {
    }

    public SessionTabViewModel(SessionCoordinator coordinator, Action end, Action resetProgress)
    {
        CoordinatorReady = true;
        _coordinator = coordinator;
        _end = end;
        _resetProgress = resetProgress;
        coordinator.StateChanged += RaiseSwitchVisibility;
    }

    public bool CoordinatorReady { get; }

    public bool ShowUnavailableNote => !CoordinatorReady;

    public string UnavailableNote =>
        "Session controls come alive once a real session is running (start one from the config screen).";

    public void RestartGame() => _coordinator?.RestartGame();

    public void SwitchGame() => SwitchTo(null);

    public void SwitchGameAsChild() => SwitchTo(1);

    public void SwitchGameAsAdult() => SwitchTo(0);

    public void EndSession() => _end?.Invoke();

    public void ResetProgress()
    {
        if (CoordinatorReady)
        {
            OnResetClicked();
        }
    }

    public void OpenGameMenu() => _coordinator?.OpenInGameMenu();

    public void ResetSpeed() => SpeedPercent = 100;

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
