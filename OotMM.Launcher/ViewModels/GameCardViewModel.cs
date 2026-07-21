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

    public GameCardViewModel(bool isOot, IFilePicker picker, Action onChanged)
    {
        IsOot = isOot;
        _picker = picker;
        _onChanged = onChanged;
        BrowseExeCommand = new RelayCommand(() => _ = BrowseExeAsync());
    }

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

    /// <summary>Re-lists the mods dir; the on-disk state (.disabled renames) is
    /// the sole source of the enabled flags, so rescans can never lose them.</summary>
    public void RefreshMods()
    {
        var scanned = ModScanner.Scan(ExePath);
        if (scanned.Count == 0 && Mods.Count > 0 && !ExeExists)
        {
            return; // transient exe/drive hiccup: keep the last known list
        }

        Mods.Clear();
        foreach (var info in scanned)
        {
            ModEntryViewModel entry = null!;
            entry = new ModEntryViewModel(info,
                onToggled: () => OnModToggled(entry),
                moveUp: () => MoveMod(entry, -1),
                moveDown: () => MoveMod(entry, +1));
            Mods.Add(entry);
        }

        UpdateMoveFlags();
        Raise(nameof(HasMods));
        Raise(nameof(ModsSummary));
    }

    /// <summary>Reorders a mod one slot and persists loadorder.txt immediately,
    /// like the original launcher; the games pick it up on their next boot.</summary>
    private void MoveMod(ModEntryViewModel entry, int delta)
    {
        var index = Mods.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Mods.Count)
        {
            return;
        }

        Mods.Move(index, target);
        UpdateMoveFlags();
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
