using System.Collections.ObjectModel;
using System.IO;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// One of the two per-game panels on the config screen: exe path + mods list.
/// The owning ConfigViewModel supplies change notification (validation and
/// persistence run there, where the full form is visible).
/// </summary>
public sealed class GameCardViewModel : ViewModelBase
{
    private readonly IFilePicker _picker;
    private readonly Action _onChanged;
    private string _exePath = "";
    private IReadOnlyList<string> _missingArchives = [];
    private string _loadOrderError = "";
    private string _modsSignature = "";
    private bool _portAdultModel = true;
    private bool _portChildModel;
    private string _modelPortStatus = "";

    public GameCardViewModel(bool isOot, IFilePicker picker, Action onChanged)
    {
        IsOot = isOot;
        _picker = picker;
        _onChanged = onChanged;
        BrowseExeCommand = new RelayCommand(() => _ = BrowseExeAsync());
        MoveSelectedTopCommand = new RelayCommand(() => MoveSelectedToEdge(top: true));
        MoveSelectedUpCommand = new RelayCommand(() => MoveSelectedByOne(-1));
        MoveSelectedDownCommand = new RelayCommand(() => MoveSelectedByOne(+1));
        MoveSelectedBottomCommand = new RelayCommand(() => MoveSelectedToEdge(top: false));
    }

    public RelayCommand MoveSelectedTopCommand { get; }
    public RelayCommand MoveSelectedUpCommand { get; }
    public RelayCommand MoveSelectedDownCommand { get; }
    public RelayCommand MoveSelectedBottomCommand { get; }

    public bool IsOot { get; }

    public string Title => IsOot ? "Ocarina of Time" : "Majora's Mask";

    public string PortSubtitle => IsOot ? "Ship of Harkinian" : "2 Ship 2 Harkinian";

    public string ExeHint => IsOot ? "path to soh.exe" : "path to 2ship.exe";

    public RelayCommand BrowseExeCommand { get; }

    public ObservableCollection<ModEntryViewModel> Mods { get; } = [];

    public bool HasMods => Mods.Count > 0;

    public string ModsSummary =>
        Mods.Count == 0 ? "Mods" : $"Mods ({Mods.Count(m => m.Enabled)} of {Mods.Count} enabled)";

    public string LoadOrderError
    {
        get => _loadOrderError;
        private set
        {
            if (Set(ref _loadOrderError, value))
            {
                Raise(nameof(HasLoadOrderError));
            }
        }
    }

    public bool HasLoadOrderError => LoadOrderError.Length > 0;

    /// <summary>MM-card-only toggle: adult Link in MM renders from the player's OoT model.</summary>
    public bool PortAdultModel
    {
        get => _portAdultModel;
        set
        {
            if (Set(ref _portAdultModel, value))
            {
                _onChanged();
            }
        }
    }

    /// <summary>MM-card-only toggle: MM's human form renders from the player's OoT child model.</summary>
    public bool PortChildModel
    {
        get => _portChildModel;
        set
        {
            if (Set(ref _portChildModel, value))
            {
                _onChanged();
            }
        }
    }

    /// <summary>Launch-time port outcome line; set by the owner, shown under the toggles.</summary>
    public string ModelPortStatus
    {
        get => _modelPortStatus;
        set
        {
            if (Set(ref _modelPortStatus, value))
            {
                Raise(nameof(HasModelPortStatus));
            }
        }
    }

    public bool HasModelPortStatus => ModelPortStatus.Length > 0;

    public string ExePath
    {
        get => _exePath;
        set
        {
            if (Set(ref _exePath, value))
            {
                RefreshMods();
                _onChanged();
            }
        }
    }

    public bool ExeExists => !string.IsNullOrWhiteSpace(ExePath) && File.Exists(ExePath);

    /// <summary>Base-game archives the setup flow still has to produce; drives
    /// the SETUP button state and the per-card status line.</summary>
    public IReadOnlyList<string> MissingArchives
    {
        get => _missingArchives;
        private set
        {
            _missingArchives = value;
            Raise();
            Raise(nameof(StatusText));
            Raise(nameof(StatusIsError));
        }
    }

    public string StatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExePath))
            {
                return "No executable selected.";
            }

            if (!ExeExists)
            {
                return "Executable not found at this path.";
            }

            return MissingArchives.Count > 0
                ? $"Setup pending — missing {string.Join(", ", MissingArchives)}"
                : "Ready.";
        }
    }

    public bool StatusIsError => !string.IsNullOrWhiteSpace(ExePath) && !ExeExists;

    /// <summary>Recomputes setup state; called by the owner whenever the seed
    /// changes because an MQ seed adds oot-mq.o2r to OoT's requirements.</summary>
    public void RefreshSetupState(bool seedNeedsMq)
    {
        MissingArchives = GameSetup.MissingArchives(ExePath, IsOot, IsOot && seedNeedsMq);
        Raise(nameof(ExeExists));
    }

    /// <summary>Disk (files + loadorder.txt) is the sole source of truth, re-read fresh every
    /// call; the view only rebuilds on real change, so rescans cannot disturb selection.</summary>
    public void RefreshMods()
    {
        var scanned = ModScanner.Scan(ExePath);
        if (scanned.Count == 0 && Mods.Count > 0 && !ExeExists)
        {
            return; // transient exe/drive hiccup: keep the last known list
        }

        var signature = string.Join("\n", scanned.Select(m => $"{m.Name}|{m.EnabledOnDisk}|{m.Required}"));
        if (signature == _modsSignature)
        {
            return;
        }

        _modsSignature = signature;
        var selected = Mods.Where(m => m.IsSelected).Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Mods.Clear();
        foreach (var info in scanned)
        {
            ModEntryViewModel entry = null!;
            entry = new ModEntryViewModel(info,
                onToggled: () => OnModToggled(entry),
                moveUp: () => MoveMod(entry, -1),
                moveDown: () => MoveMod(entry, +1));
            entry.IsSelected = selected.Contains(info.Name);
            Mods.Add(entry);
        }

        UpdateMoveFlags();
        Raise(nameof(HasMods));
        Raise(nameof(ModsSummary));
    }

    /// <summary>Reorders a mod one slot; the games pick the manifest up on their next boot.</summary>
    private void MoveMod(ModEntryViewModel entry, int delta)
    {
        var index = Mods.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Mods.Count)
        {
            return;
        }

        Mods.Move(index, target);
        PersistOrder();
    }

    // A selected mod resting against the edge or another selected one stays put, so a
    // scattered selection converges into a block instead of jamming.
    private void MoveSelectedByOne(int delta)
    {
        var moved = false;
        if (delta < 0)
        {
            for (var i = 1; i < Mods.Count; i++)
            {
                if (Mods[i].IsSelected && !Mods[i - 1].IsSelected)
                {
                    Mods.Move(i, i - 1);
                    moved = true;
                }
            }
        }
        else
        {
            for (var i = Mods.Count - 2; i >= 0; i--)
            {
                if (Mods[i].IsSelected && !Mods[i + 1].IsSelected)
                {
                    Mods.Move(i, i + 1);
                    moved = true;
                }
            }
        }

        if (moved)
        {
            PersistOrder();
        }
    }

    private void MoveSelectedToEdge(bool top)
    {
        var selected = Mods.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0 || selected.Count == Mods.Count)
        {
            return;
        }

        var rest = Mods.Where(m => !m.IsSelected).ToList();
        var reordered = top ? selected.Concat(rest) : rest.Concat(selected);
        ReplaceOrder(reordered.ToList());
    }

    /// <summary>Drop target for drag reordering: inserts the selected block at
    /// <paramref name="insertIndex"/> (an index into the current list, 0..Count).</summary>
    public void MoveSelectedTo(int insertIndex)
    {
        var selected = Mods.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var rest = Mods.Where(m => !m.IsSelected).ToList();
        // The insert position was measured against the full list; count only unselected rows
        // above it, because the selected ones are being pulled out.
        var restIndex = Math.Clamp(
            Mods.Take(Math.Clamp(insertIndex, 0, Mods.Count)).Count(m => !m.IsSelected),
            0, rest.Count);
        rest.InsertRange(restIndex, selected);
        ReplaceOrder(rest);
    }

    private void ReplaceOrder(IReadOnlyList<ModEntryViewModel> reordered)
    {
        if (reordered.SequenceEqual(Mods))
        {
            return;
        }

        for (var target = 0; target < reordered.Count; target++)
        {
            var current = Mods.IndexOf(reordered[target]);
            if (current != target)
            {
                Mods.Move(current, target);
            }
        }

        PersistOrder();
    }

    // Written from the full current list, so new archives get ranked where they appear and
    // vanished ones drop out: the manifest converges to disk.
    private void PersistOrder()
    {
        UpdateMoveFlags();
        // The next rescan will read back exactly this order; matching the signature now keeps it
        // from rebuilding the list (and disturbing the selection) over our own write.
        _modsSignature = string.Join("\n", Mods.Select(m => $"{m.Name}|{m.Enabled}|{m.Required}"));
        LoadOrderError = ModScanner.SaveLoadOrder(ExePath, Mods.Select(m => m.Name))
            ? ""
            : "Could not write loadorder.txt in the mods folder.";
    }

    private void UpdateMoveFlags()
    {
        for (var i = 0; i < Mods.Count; i++)
        {
            Mods[i].IsFirst = i == 0;
            Mods[i].IsLast = i == Mods.Count - 1;
        }
    }

    private void OnModToggled(ModEntryViewModel entry)
    {
        LoadOrderError = ModScanner.SetEnabledOnDisk(entry.Info, entry.Enabled)
            ? ""
            : $"Could not rename {entry.Name} in the mods folder.";
        RefreshMods();
        Raise(nameof(ModsSummary));
        _onChanged();
    }

    private async Task BrowseExeAsync()
    {
        var picked = await _picker.PickFileAsync(
            $"Select the {Title} port executable",
            "Game executable",
            OperatingSystem.IsWindows() ? "*.exe" : "*");
        if (picked is not null)
        {
            ExePath = picked;
        }
    }
}
