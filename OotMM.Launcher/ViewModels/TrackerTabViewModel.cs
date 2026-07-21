namespace OotMM.Launcher.ViewModels;

/// <summary>One check in the tracker list.</summary>
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

    /// <summary>"OOT" or "MM" — shown as the row's leading badge.</summary>
    public string GameTag { get; }

    public string Name { get; }

    public string CheckId { get; }

    /// <summary>What was found here, revealed once collected (empty until then).</summary>
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

    /// <summary>Reachable with current items per the reverse-logic solver.</summary>
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

    /// <summary>Unlock order sent; waiting for the game's collection echo.</summary>
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

/// <summary>The open-checks tracker tab: stats, filters, and the (dev-mode)
/// force-unlock cheat over the full check list.</summary>
public sealed class TrackerTabViewModel : ViewModelBase
{
    private List<CheckRowViewModel> _allChecks = [];
    private string _filterText = "";
    private int _gameFilterIndex; // 0 = both, 1 = OoT, 2 = MM
    // The play view by default: what is still to do, and reachable right now.
    private bool _hideCollected = true;
    private bool _onlyInLogic = true;
    private bool _devMode;
    private bool _forceUnlockMode;
    private CheckRowViewModel? _armedRow;
    private DateTime _armedAt;
    private int _inLogicCount;
    private bool _solverLoaded;
    private bool _isMultiworld;

    /// <summary>Sends a force-unlock order for a check id; false when the game
    /// cannot take it right now.</summary>
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

    // Zero-in-logic with open checks is a real multiworld state: everything
    // reachable is swept and the next progression item is in another world.
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

    /// <summary>Cheat interaction: click once to arm, click the same row again
    /// within 5s to send the unlock order.</summary>
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

    /// <summary>Entry point for the Tracker module port: replaces the model and
    /// re-runs the active filters.</summary>
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

    /// <summary>Call after mutating row Collected/InLogic flags in place.</summary>
    public void NotifyCheckStatesChanged() => RaiseListChanged();

    /// <summary>Representative rows for the layout preview (real check names
    /// from the seed format, hand-picked states to show every row style).</summary>
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
