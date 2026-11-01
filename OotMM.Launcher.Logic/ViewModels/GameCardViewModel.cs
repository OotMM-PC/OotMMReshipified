using System.Collections.ObjectModel;
using System.IO;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;

namespace OotMM.Launcher.ViewModels;

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
    }

    public void BrowseExe() => _ = BrowseExeAsync();

    public void MoveSelectedTop() => MoveSelectedToEdge(top: true);

    public void MoveSelectedUp() => MoveSelectedByOne(-1);

    public void MoveSelectedDown() => MoveSelectedByOne(+1);

    public void MoveSelectedBottom() => MoveSelectedToEdge(top: false);

    public bool IsOot { get; }

    public string Title => IsOot ? "Ocarina of Time" : "Majora's Mask";

    public string PortSubtitle => IsOot ? "Ship of Harkinian" : "2 Ship 2 Harkinian";

    public string ExeHint => IsOot ? "path to soh.exe" : "path to 2ship.exe";

    public ObservableCollection<ModEntryViewModel> Mods { get; } = [];

    public bool HasMods => Mods.Count > 0;

    public string ModsSummary =>
        Mods.Count == 0 ? "Mods" : $"Mods ({Mods.Count(m => m.Enabled)} of {Mods.Count} enabled)";

    public int EnabledModCount => Mods.Count(m => m.Enabled && !m.Required);

    public bool IsReady => ExeExists && MissingArchives.Count == 0;

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

    public IReadOnlyList<string> MissingArchives
    {
        get => _missingArchives;
        private set
        {
            _missingArchives = value;
            Raise();
            Raise(nameof(StatusText));
            Raise(nameof(StatusIsError));
            Raise(nameof(IsReady));
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

    public void RefreshSetupState(bool seedNeedsMq)
    {
        MissingArchives = GameSetup.MissingArchives(ExePath, IsOot, IsOot && seedNeedsMq);
        Raise(nameof(ExeExists));
    }

    public void RefreshMods()
    {
        var scanned = ModScanner.Scan(ExePath);
        if (scanned.Count == 0 && Mods.Count > 0 && !ExeExists)
        {
            return;
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
        Raise(nameof(EnabledModCount));
    }

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

    public void MoveSelectedTo(int insertIndex)
    {
        var selected = Mods.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var rest = Mods.Where(m => !m.IsSelected).ToList();
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

    private void PersistOrder()
    {
        UpdateMoveFlags();
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
        Raise(nameof(EnabledModCount));
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
