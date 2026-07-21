namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Session controls: restart the active game, switch games, end the session,
/// reset seed progress. Every action here needs the process coordinator, so
/// the commands exist (the views bind them) but report unavailable until that
/// module is ported — CanExecute keeps the buttons visibly disabled.
/// </summary>
public sealed class SessionTabViewModel : ViewModelBase
{
    /// <summary>Preview mode: laid out but disabled.</summary>
    public SessionTabViewModel()
    {
        RestartGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        SwitchGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        EndSessionCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        ResetProgressCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        OpenGameMenuCommand = new RelayCommand(() => { }, () => CoordinatorReady);
    }

    /// <summary>Live mode: wired to the session coordinator.</summary>
    public SessionTabViewModel(Action restart, Action switchGame, Action end, Action reset,
                               Action openGameMenu)
    {
        CoordinatorReady = true;
        RestartGameCommand = new RelayCommand(restart);
        SwitchGameCommand = new RelayCommand(switchGame);
        EndSessionCommand = new RelayCommand(end);
        ResetProgressCommand = new RelayCommand(reset);
        OpenGameMenuCommand = new RelayCommand(openGameMenu);
    }

    public bool CoordinatorReady { get; }

    public bool ShowUnavailableNote => !CoordinatorReady;

    public string UnavailableNote =>
        "Session controls come alive once a real session is running (start one from the config screen).";

    public RelayCommand RestartGameCommand { get; }
    public RelayCommand SwitchGameCommand { get; }
    public RelayCommand EndSessionCommand { get; }
    public RelayCommand ResetProgressCommand { get; }
    public RelayCommand OpenGameMenuCommand { get; }
}
