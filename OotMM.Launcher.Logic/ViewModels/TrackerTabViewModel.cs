using System.Collections.ObjectModel;

using OotMM.Core;

namespace OotMM.Launcher.ViewModels;

public sealed class CheckRowViewModel : ViewModelBase
{
    private bool _collected;
    private bool _inLogic;
    private bool _isArmed;
    private bool _isPending;

    public CheckRowViewModel(string gameTag, string name, string itemText,
                             string checkId = "", bool ownerIsLocal = true)
    {
        GameTag = gameTag;
        Name = name;
        ItemText = itemText + (ownerIsLocal || itemText.Length == 0 ? "" : " (another player's item)");
        CheckId = checkId;
    }

    internal static CheckRowViewModel Empty { get; } = new("", "", "");

    public string GameTag { get; }

    public string Name { get; }

    public string CheckId { get; }

    public string ItemText { get; }

    public ContentClass Content { get; init; }

    public bool Required { get; init; }

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
    private int _ootContentFilter;
    private int _mmContentFilter;
    private bool _ootRevealed;
    private bool _mmRevealed;
    private bool _separateStones;
    private bool _hasRequiredData;
    private bool _contentFiltersEnabled;
    private string _ootContentLabels = "Any contents";
    private string _mmContentLabels = "Any contents";
    private int _scrollResetToken;

    private static readonly (string Label, ContentClass Kind)[] ContentKinds =
    [
        ("Major items", ContentClass.Major),
        ("Small keys", ContentClass.Key),
        ("Boss keys", ContentClass.BossKey),
        ("Skulltula tokens", ContentClass.Spider),
        ("Stray fairies", ContentClass.Fairy),
        ("Hearts", ContentClass.Heart),
        ("Souls", ContentClass.Soul),
        ("Maps and compasses", ContentClass.MapCompass),
    ];

    internal Func<string, bool>? ForceUnlockSender { get; set; }

    /// <summary>Changes whenever the user changes a filter, so the list can jump back to the top only then.</summary>
    public int ScrollResetToken => _scrollResetToken;

    private void FiltersChanged()
    {
        _scrollResetToken++;
        Raise(nameof(ScrollResetToken));
        RaiseListChanged();
    }

    /// <summary>Whether the player turned the contents filters on and the session allows them.</summary>
    public bool ContentFiltersEnabled
    {
        get => _contentFiltersEnabled;
        set
        {
            if (!Set(ref _contentFiltersEnabled, value))
            {
                return;
            }

            if (!value)
            {
                _ootContentFilter = 0;
                _mmContentFilter = 0;
                Raise(nameof(OotContentFilterMask));
                Raise(nameof(MmContentFilterMask));
            }

            Raise(nameof(ShowOotContentFilter));
            Raise(nameof(ShowMmContentFilter));
            FiltersChanged();
        }
    }

    /// <summary>The filter choices for a game, each with how many checks the list would show for it, joined by "|".</summary>
    public string OotContentLabels => _ootContentLabels;

    public string MmContentLabels => _mmContentLabels;

    private string ContentLabels(string gameTag, bool counted, string[] terms)
    {
        var required = 0;
        var perKind = new int[ContentKinds.Length];
        if (counted)
        {
            foreach (var row in _allChecks)
            {
                if (row.GameTag != gameTag || !PassesToggles(row) || !MatchesSearch(row, terms))
                {
                    continue;
                }

                required += row.Required ? 1 : 0;
                var kind = Array.FindIndex(ContentKinds, k => k.Kind == row.Content);
                if (kind >= 0)
                {
                    perKind[kind]++;
                }
            }
        }

        var labels = new List<string> { "Any contents" };
        if (_hasRequiredData)
        {
            labels.Add(counted ? $"Way of the Hero ({required})" : "Way of the Hero");
        }

        labels.AddRange(ContentKinds.Select((kind, i) => counted ? $"{kind.Label} ({perKind[i]})" : kind.Label));
        return string.Join("|", labels);
    }

    private void RefreshContentLabels()
    {
        var terms = SearchTerms();
        var oot = ContentLabels("OOT", _contentFiltersEnabled && _ootRevealed, terms);
        if (oot != _ootContentLabels)
        {
            _ootContentLabels = oot;
            Raise(nameof(OotContentLabels));
        }

        var mm = ContentLabels("MM", _contentFiltersEnabled && _mmRevealed, terms);
        if (mm != _mmContentLabels)
        {
            _mmContentLabels = mm;
            Raise(nameof(MmContentLabels));
        }
    }

    private bool PassesToggles(CheckRowViewModel row) =>
        (!_hideCollected || !row.Collected) && (!_onlyInLogic || row.InLogic || row.Collected);

    private string[] SearchTerms() =>
        _filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Fold).Where(t => t.Length > 0).ToArray();

    /// <summary>The selected filter choices for a game, one bit per choice index; 0 means any contents.</summary>
    public int OotContentFilterMask
    {
        get => _ootContentFilter;
        set => SetContentFilter(ref _ootContentFilter, _contentFiltersEnabled && _ootRevealed, value,
                                nameof(OotContentFilterMask));
    }

    public int MmContentFilterMask
    {
        get => _mmContentFilter;
        set => SetContentFilter(ref _mmContentFilter, _contentFiltersEnabled && _mmRevealed, value,
                                nameof(MmContentFilterMask));
    }

    public bool OotContentUnlocked => _ootRevealed;

    public bool MmContentUnlocked => _mmRevealed;

    public string OotContentLockedText => LockedText("OoT");

    public string MmContentLockedText => LockedText("MM");

    public bool ShowOotContentFilter => _contentFiltersEnabled && _gameFilterIndex != 2;

    public bool ShowMmContentFilter => _contentFiltersEnabled && _gameFilterIndex != 1;

    private string LockedText(string game) =>
        _separateStones ? $"Locked: find the {game} Stone of Agony"
        : "Locked: find the Stone of Agony";

    private void SetContentFilter(ref int field, bool unlocked, int value, string name)
    {
        var choices = ContentKinds.Length + (_hasRequiredData ? 2 : 1);
        var next = unlocked ? value & (((1 << choices) - 1) & ~1) : 0;
        if (field != next)
        {
            field = next;
            Raise(name);
            FiltersChanged();
        }
    }

    /// <summary>Sets which games currently reveal what their checks hold, and whether each game has its own stone.</summary>
    public void SetContentAccess(bool oot, bool mm, bool separateStones)
    {
        if (oot == _ootRevealed && mm == _mmRevealed && separateStones == _separateStones)
        {
            return;
        }

        _ootRevealed = oot;
        _mmRevealed = mm;
        _separateStones = separateStones;
        if (!oot && _ootContentFilter != 0)
        {
            _ootContentFilter = 0;
            Raise(nameof(OotContentFilterMask));
        }

        if (!mm && _mmContentFilter != 0)
        {
            _mmContentFilter = 0;
            Raise(nameof(MmContentFilterMask));
        }

        Raise(nameof(OotContentUnlocked));
        Raise(nameof(MmContentUnlocked));
        Raise(nameof(OotContentLockedText));
        Raise(nameof(MmContentLockedText));
        RaiseListChanged();
    }

    private bool MatchesContents(CheckRowViewModel row)
    {
        var mask = row.GameTag == "OOT" ? _ootContentFilter : _mmContentFilter;
        if (mask == 0)
        {
            return true;
        }

        var first = 1;
        if (_hasRequiredData)
        {
            if ((mask & 2) != 0 && row.Required)
            {
                return true;
            }

            first = 2;
        }

        var kind = Array.FindIndex(ContentKinds, k => k.Kind == row.Content);
        return kind >= 0 && (mask & (1 << (first + kind))) != 0;
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value))
            {
                FiltersChanged();
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
                Raise(nameof(ShowOotContentFilter));
                Raise(nameof(ShowMmContentFilter));
                FiltersChanged();
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
                FiltersChanged();
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
                FiltersChanged();
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

    public ObservableCollection<CheckRowViewModel> VisibleChecks { get; private set; } = [];

    private int _hiddenMatches;

    public int HiddenMatches => _hiddenMatches;

    public string HiddenMatchesText =>
        $"{_hiddenMatches} more match{(_hiddenMatches == 1 ? " is" : "es are")} hidden by the filters.";

    public void ShowAllMatches()
    {
        _hideCollected = false;
        _onlyInLogic = false;
        Raise(nameof(HideCollected));
        Raise(nameof(OnlyInLogic));
        FiltersChanged();
    }

    private static string Fold(string text) =>
        new(text.Where(c => c is not ('\'' or '’' or '-' or '—' or '.' or ',')).Select(char.ToLowerInvariant).ToArray());

    private static bool MatchesSearch(CheckRowViewModel row, string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }

        var haystack = Fold($"{row.GameTag} {(row.GameTag == "MM" ? "majora" : "ocarina")} {row.Name} {(row.Collected ? row.ItemText : "")}");
        return terms.All(haystack.Contains);
    }

    private List<CheckRowViewModel> FilterChecks()
    {
        var terms = SearchTerms();
        var matches = _allChecks.Where(r =>
            (_gameFilterIndex != 1 || r.GameTag == "OOT")
            && (_gameFilterIndex != 2 || r.GameTag == "MM")
            && MatchesContents(r)
            && MatchesSearch(r, terms)).ToList();

        var rows = matches.Where(PassesToggles).ToList();

        var hidden = matches.Count(r =>
            terms.Length > 0 || (r.GameTag == "OOT" ? _ootContentFilter : _mmContentFilter) != 0) - rows.Count(r =>
            terms.Length > 0 || (r.GameTag == "OOT" ? _ootContentFilter : _mmContentFilter) != 0);
        if (hidden != _hiddenMatches)
        {
            _hiddenMatches = hidden;
            Raise(nameof(HiddenMatches));
            Raise(nameof(HiddenMatchesText));
        }

        return rows;
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

    public CheckRowViewModel CheckAt(int index) =>
        index >= 0 && index < VisibleChecks.Count ? VisibleChecks[index] : CheckRowViewModel.Empty;

    /// <summary>A stable identity for the visible row at an index, for restoring the scroll position.</summary>
    public string KeyAt(int index) =>
        index >= 0 && index < VisibleChecks.Count ? RowKey(VisibleChecks[index]) : "";

    public int IndexOfKey(string key)
    {
        for (var i = 0; i < VisibleChecks.Count; i++)
        {
            if (RowKey(VisibleChecks[i]) == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static string RowKey(CheckRowViewModel row) => row.GameTag + ":" + row.CheckId;

    public void ClickVisible(int index)
    {
        if (index >= 0 && index < VisibleChecks.Count)
        {
            OnRowClicked(VisibleChecks[index]);
        }
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
            if (ForceUnlockSender?.Invoke(row.GameTag.ToLowerInvariant() + ":" + row.CheckId) == true)
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

    public void SetChecks(IEnumerable<CheckRowViewModel> checks, bool hasRequiredData = false)
    {
        _allChecks = checks.ToList();
        if (hasRequiredData != _hasRequiredData)
        {
            _hasRequiredData = hasRequiredData;
            _ootContentFilter = 0;
            _mmContentFilter = 0;
            Raise(nameof(OotContentFilterMask));
            Raise(nameof(MmContentFilterMask));
        }

        RaiseListChanged();
    }

    private void RaiseListChanged()
    {
        var rows = FilterChecks();
        if (!rows.SequenceEqual(VisibleChecks))
        {
            VisibleChecks = new ObservableCollection<CheckRowViewModel>(rows);
            Raise(nameof(VisibleChecks));
        }

        RefreshContentLabels();
        Raise(nameof(SummaryText));
        Raise(nameof(CollectedFraction));
    }

    public void NotifyCheckStatesChanged() => RaiseListChanged();

    public void LoadSampleData()
    {
        CheckRowViewModel Row(string tag, string name, string item, bool collected, bool inLogic,
                              ContentClass content = ContentClass.Normal) =>
            new(tag, name, item, checkId: name) { Collected = collected, InLogic = inLogic, Content = content };

        SetChecks(
        [
            Row("OOT", "Deku Tree — Map Chest", "Kokiri Sword", true, true, ContentClass.Major),
            Row("OOT", "Market — Mask Shop Delivery", "MM Song of Soaring", true, true, ContentClass.Major),
            Row("OOT", "Hyrule Field — Ocarina of Time", "", false, true, ContentClass.Major),
            Row("OOT", "Kakariko — Anju's Cuccos", "", false, true),
            Row("OOT", "Dodongo's Cavern — Boss Heart Container", "", false, false),
            Row("MM", "Clock Town — Postbox", "", false, true),
            Row("MM", "Great Bay — Zora Cape Heart Piece", "", false, false),
            Row("MM", "Woodfall Temple — Boss Key Chest", "", false, false, ContentClass.Key),
            Row("MM", "Astral Observatory — Moon's Tear", "OOT Slingshot", true, true),
            Row("MM", "Southern Swamp — Kotake's Mushroom Trade", "", false, true),
        ]);
        UpdateStats(4, solverLoaded: true, isMultiworld: false);
        SetContentAccess(oot: true, mm: true, separateStones: true);
    }
}
