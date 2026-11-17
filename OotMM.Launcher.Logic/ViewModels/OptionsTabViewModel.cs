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
    private bool _holdToZTarget;
    private bool _remoteItemPrompts = true;
    private bool _fairySpotPads;
    private bool _contentFilters;
    private string _contentFiltersNote = "";
    private bool _contentFiltersAvailable = true;
    private bool _potRolling = true;
    private bool _entranceMap = true;
    private string _entranceMapNote = "";

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

    internal Action<bool>? HoldToZTargetChanged { get; set; }

    public bool HoldToZTarget
    {
        get => _holdToZTarget;
        set
        {
            if (Set(ref _holdToZTarget, value))
            {
                HoldToZTargetChanged?.Invoke(value);
            }
        }
    }

    internal void ShowHoldToZTarget(bool hold) => Set(ref _holdToZTarget, hold, nameof(HoldToZTarget));

    internal Action<bool>? RemoteItemPromptsChanged { get; set; }

    public bool RemoteItemPrompts
    {
        get => _remoteItemPrompts;
        set
        {
            if (Set(ref _remoteItemPrompts, value))
            {
                RemoteItemPromptsChanged?.Invoke(value);
            }
        }
    }

    internal Action<bool>? FairySpotPadsChanged { get; set; }

    public bool FairySpotPads
    {
        get => _fairySpotPads;
        set
        {
            if (Set(ref _fairySpotPads, value))
            {
                FairySpotPadsChanged?.Invoke(value);
            }
        }
    }

    internal Action<bool>? PotRollingChanged { get; set; }

    public bool PotRolling
    {
        get => _potRolling;
        set
        {
            if (Set(ref _potRolling, value))
            {
                PotRollingChanged?.Invoke(value);
            }
        }
    }

    internal Action<bool>? EntranceMapChanged { get; set; }

    public bool EntranceMap
    {
        get => _entranceMap;
        set
        {
            if (Set(ref _entranceMap, value))
            {
                EntranceMapChanged?.Invoke(value);
            }
        }
    }

    /// <summary>Why the entrance map cannot be used right now, or empty when it can.</summary>
    public string EntranceMapNote
    {
        get => _entranceMapNote;
        internal set
        {
            if (Set(ref _entranceMapNote, value))
            {
                Raise(nameof(EntranceMapAllowed));
            }
        }
    }

    public bool EntranceMapAllowed => _entranceMapNote.Length == 0;

    internal Action<bool>? ContentFiltersChanged { get; set; }

    public bool ContentFilters
    {
        get => _contentFilters;
        set
        {
            if (Set(ref _contentFilters, value))
            {
                ContentFiltersChanged?.Invoke(value);
            }
        }
    }

    /// <summary>Why the contents filters cannot be used right now, or empty when they can.</summary>
    public string ContentFiltersNote
    {
        get => _contentFiltersNote;
        internal set
        {
            if (Set(ref _contentFiltersNote, value))
            {
                Raise(nameof(ContentFiltersAllowed));
            }
        }
    }

    public bool ContentFiltersAllowed => _contentFiltersNote.Length == 0;

    /// <summary>False on seeds that never show what checks hold, where the option has no use.</summary>
    public bool ContentFiltersAvailable
    {
        get => _contentFiltersAvailable;
        internal set => Set(ref _contentFiltersAvailable, value);
    }

    public bool CheatGatePending
    {
        get => _cheatGatePending;
        set => Set(ref _cheatGatePending, value);
    }

    internal Action? RestartGame { get; set; }

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
