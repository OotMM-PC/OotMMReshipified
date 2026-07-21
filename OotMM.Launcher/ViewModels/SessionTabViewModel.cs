namespace OotMM.Launcher.ViewModels;

/// <summary>
/// Session controls: restart the active game, switch games, end the session,
/// reset seed progress. Every action here needs the process coordinator, so
/// the commands exist (the views bind them) but report unavailable until that
/// module is ported — CanExecute keeps the buttons visibly disabled.
/// </summary>
public sealed class SessionTabViewModel : ViewModelBase
{
    public SessionTabViewModel()
    {
        // All gated off until the coordinator exists; flipping CoordinatorReady
        // to true (and wiring real delegates) is the integration point.
        RestartGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        SwitchGameCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        EndSessionCommand = new RelayCommand(() => { }, () => CoordinatorReady);
        ResetProgressCommand = new RelayCommand(() => { }, () => CoordinatorReady);
    }

    /// <summary>False until the GameProcess/coordinator modules are ported and
    /// injected; the tab explains this in its footer note.</summary>
    public bool CoordinatorReady => false;

    public string UnavailableNote =>
        "Session controls come alive with the coordinator module — they are laid out "
        + "here so the runtime screen is complete.";

    public RelayCommand RestartGameCommand { get; }
    public RelayCommand SwitchGameCommand { get; }
    public RelayCommand EndSessionCommand { get; }
    public RelayCommand ResetProgressCommand { get; }
}
