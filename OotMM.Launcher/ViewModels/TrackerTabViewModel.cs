namespace OotMM.Launcher.ViewModels;

public sealed class CheckRowViewModel : ViewModelBase
{
    private readonly Action<CheckRowViewModel>? _clicked;
    private bool _collected;
    private bool _inLogic;
    private bool _isArmed;
    private bool _isPending;

    public CheckRowViewModel(string gameTag, string name, string itemText,
                             string checkId = "", bool ownerIsLocal = true,
                             Action<CheckRowViewModel>? clicked = null)
    {
        GameTag = gameTag;
        Name = name;
        ItemText = itemText + (ownerIsLocal || itemText.Length == 0 ? "" : " (another player's item)");
        CheckId = checkId;
        _clicked = clicked;
        ClickCommand = new RelayCommand(() => _clicked?.Invoke(this));
    }

    public string GameTag { get; }

    public string Name { get; }

    public string CheckId { get; }

    public string ItemText { get; }

    public RelayCommand ClickCommand { get; }

    public bool Collected
    {
        get => _collected;
        set
        {
            if (Set(ref _collected, value) && value)
            {
                IsPending = false;
            }
        }
    }

    public bool InLogic
    {
        get => _inLogic;
        set => Set(ref _inLogic, value);
    }

    public bool IsArmed
    {
        get => _isArmed;
        set
        {
            if (Set(ref _isArmed, value))
            {
                Raise(nameof(DisplayName));
            }
        }
    }

    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (Set(ref _isPending, value))
            {
                Raise(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => IsArmed ? $"{Name}  « CLICK TO CONFIRM »"
        : IsPending ? $"{Name}  (unlocking…)"
        : Name;
}

public sealed class TrackerTabViewModel : ViewModelBase
{
    private List<CheckRowViewModel> _allChecks = [];
    private string _filterText = "";
    private int _gameFilterIndex;
    private bool _hideCollected = true;
    private bool _onlyInLogic = true;
    private bool _devMode;
    private bool _forceUnlockMode;
    private CheckRowViewModel? _armedRow;
    private DateTime _armedAt;
    private int _inLogicCount;
    private bool _solverLoaded;
    private bool _isMultiworld;

    public Func<string, bool>? ForceUnlockSender { get; set; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value))
            {
                RaiseListChanged();
            }
        }
    }

    public int GameFilterIndex
    {
        get => _gameFilterIndex;
        set
        {
            if (Set(ref _gameFilterIndex, value))
            {
                RaiseListChanged();
            }
        }
    }

    public bool HideCollected
    {
        get => _hideCollected;
        set
        {
            if (Set(ref _hideCollected, value))
            {
                RaiseListChanged();
            }
        }
    }

    public bool OnlyInLogic
    {
        get => _onlyInLogic;
        set
        {
            if (Set(ref _onlyInLogic, value))
            {
                RaiseListChanged();
            }
        }
    }

    public bool DevMode
    {
        get => _devMode;
        set
        {
            if (Set(ref _devMode, value) && !value)
            {
                ForceUnlockMode = false;
            }
        }
    }

    public bool ForceUnlockMode
    {
        get => _forceUnlockMode;
        set
        {
            if (Set(ref _forceUnlockMode, value))
            {
                Disarm();
            }
        }
    }

    public IReadOnlyList<CheckRowViewModel> VisibleChecks
    {
        get
        {
            IEnumerable<CheckRowViewModel> rows = _allChecks;
            if (_gameFilterIndex == 1)
            {
                rows = rows.Where(r => r.GameTag == "OOT");
            }
            else if (_gameFilterIndex == 2)
            {
                rows = rows.Where(r => r.GameTag == "MM");
            }

            if (_hideCollected)
            {
                rows = rows.Where(r => !r.Collected);
            }

            if (_onlyInLogic)
            {
                rows = rows.Where(r => r.InLogic || r.Collected);
            }

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                rows = rows.Where(r =>
                    r.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase));
            }

            return rows.ToList();
        }
    }

    public double CollectedFraction =>
        _allChecks.Count > 0 ? (double)_allChecks.Count(r => r.Collected) / _allChecks.Count : 0;

    public string SummaryText =>
        $"{_allChecks.Count(r => r.Collected)} / {_allChecks.Count} checks collected";

    public string InLogicText => _solverLoaded
        ? $"{_inLogicCount} check{(_inLogicCount == 1 ? "" : "s")} in logic right now"
        : "No logic data in this seed — regenerate it to see check availability.";

    public bool ShowStuckNote => _solverLoaded && _isMultiworld && _inLogicCount == 0
                                 && _allChecks.Any(r => !r.Collected);

    public string StuckNote =>
        "Nothing reachable with your current items — your next progression item is likely in another player's world.";

    public void UpdateStats(int inLogicCount, bool solverLoaded, bool isMultiworld)
    {
        _inLogicCount = inLogicCount;
        _solverLoaded = solverLoaded;
        _isMultiworld = isMultiworld;
        Raise(nameof(InLogicText));
        Raise(nameof(ShowStuckNote));
    }

    public void OnRowClicked(CheckRowViewModel row)
    {
        if (!ForceUnlockMode || row.Collected || row.IsPending || row.CheckId.Length == 0)
        {
            return;
        }

        if (_armedRow == row && DateTime.UtcNow - _armedAt <= TimeSpan.FromSeconds(5))
        {
            Disarm();
            if (ForceUnlockSender?.Invoke(row.CheckId) == true)
            {
                row.IsPending = true;
            }

            return;
        }

        Disarm();
        _armedRow = row;
        _armedAt = DateTime.UtcNow;
        row.IsArmed = true;
    }

    private void Disarm()
    {
        if (_armedRow is not null)
        {
            _armedRow.IsArmed = false;
            _armedRow = null;
        }
    }

    public void SetChecks(IEnumerable<CheckRowViewModel> checks)
    {
        _allChecks = checks.ToList();
        RaiseListChanged();
    }

    private void RaiseListChanged()
    {
        Raise(nameof(VisibleChecks));
        Raise(nameof(SummaryText));
        Raise(nameof(CollectedFraction));
    }

    public void NotifyCheckStatesChanged() => RaiseListChanged();

    public void LoadSampleData()
    {
        CheckRowViewModel Row(string tag, string name, string item, bool collected, bool inLogic) =>
            new(tag, name, item) { Collected = collected, InLogic = inLogic };

        SetChecks(
        [
            Row("OOT", "Deku Tree — Map Chest", "Kokiri Sword", true, true),
            Row("OOT", "Market — Mask Shop Delivery", "MM Song of Soaring", true, true),
            Row("OOT", "Hyrule Field — Ocarina of Time", "", false, true),
            Row("OOT", "Kakariko — Anju's Cuccos", "", false, true),
            Row("OOT", "Dodongo's Cavern — Boss Heart Container", "", false, false),
            Row("MM", "Clock Town — Postbox", "", false, true),
            Row("MM", "Great Bay — Zora Cape Heart Piece", "", false, false),
            Row("MM", "Woodfall Temple — Boss Key Chest", "", false, false),
            Row("MM", "Astral Observatory — Moon's Tear", "OOT Slingshot", true, true),
            Row("MM", "Southern Swamp — Kotake's Mushroom Trade", "", false, true),
        ]);
        UpdateStats(4, solverLoaded: true, isMultiworld: false);
    }
}
