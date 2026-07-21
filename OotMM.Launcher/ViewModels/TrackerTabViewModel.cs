namespace OotMM.Launcher.ViewModels;

/// <summary>One check in the tracker list.</summary>
public sealed class CheckRowViewModel : ViewModelBase
{
    private bool _collected;
    private bool _inLogic;

    public CheckRowViewModel(string gameTag, string name, string itemText)
    {
        GameTag = gameTag;
        Name = name;
        ItemText = itemText;
    }

    /// <summary>"OOT" or "MM" — shown as the row's leading badge.</summary>
    public string GameTag { get; }

    public string Name { get; }

    /// <summary>What was found here, revealed once collected (empty until then).</summary>
    public string ItemText { get; }

    public bool Collected
    {
        get => _collected;
        set => Set(ref _collected, value);
    }

    /// <summary>Reachable with current items per the reverse-logic solver.</summary>
    public bool InLogic
    {
        get => _inLogic;
        set => Set(ref _inLogic, value);
    }
}

/// <summary>
/// The open-checks tracker tab: filterable list of every check in the seed.
/// The real rows come from the Tracker module port feeding SetChecks; the
/// filters are pure view logic and already final.
/// </summary>
public sealed class TrackerTabViewModel : ViewModelBase
{
    private List<CheckRowViewModel> _allChecks = [];
    private string _filterText = "";
    private int _gameFilterIndex; // 0 = both, 1 = OoT, 2 = MM
    // The play view by default: what is still to do, and reachable right now.
    private bool _hideCollected = true;
    private bool _onlyInLogic = true;

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

    public string SummaryText
    {
        get
        {
            var collected = _allChecks.Count(r => r.Collected);
            return $"{collected} / {_allChecks.Count} checks collected";
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
    }
}
