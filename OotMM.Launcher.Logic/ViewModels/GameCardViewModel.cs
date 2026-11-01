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
    private ModEntryViewModel? _selectionAnchor;

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

    public bool HasSelection => Mods.Any(m => m.IsSelected);

    public ModEntryViewModel ModAt(int index) =>
        index >= 0 && index < Mods.Count ? Mods[index] : ModEntryViewModel.Empty;

    public void SelectMod(int index, bool toggle, bool extend)
    {
        if (index < 0 || index >= Mods.Count)
        {
            return;
        }

        var target = Mods[index];
        var anchor = _selectionAnchor is not null ? Mods.IndexOf(_selectionAnchor) : -1;
        if (extend && anchor >= 0)
        {
            var (from, to) = anchor <= index ? (anchor, index) : (index, anchor);
            for (var i = 0; i < Mods.Count; i++)
            {
                var inRange = i >= from && i <= to;
                Mods[i].IsSelected = toggle ? Mods[i].IsSelected || inRange : inRange;
            }
        }
        else if (toggle)
        {
            target.IsSelected = !target.IsSelected;
            _selectionAnchor = target;
        }
        else
        {
            foreach (var mod in Mods)
            {
                mod.IsSelected = ReferenceEquals(mod, target);
            }

            _selectionAnchor = target;
        }

        Raise(nameof(HasSelection));
    }

    public void ClearSelection()
    {
        foreach (var mod in Mods)
        {
            mod.IsSelected = false;
        }

        _selectionAnchor = null;
        Raise(nameof(HasSelection));
    }

    public void SetModEnabled(int index, bool enabled)
    {
        if (index >= 0 && index < Mods.Count)
        {
            Mods[index].Enabled = enabled;
        }
    }

    public void MoveModAt(int index, int delta)
    {
        if (index >= 0 && index < Mods.Count)
        {
            MoveMod(Mods[index], delta);
        }
    }

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
        var existing = new Dictionary<string, ModEntryViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods)
        {
            existing.TryAdd(mod.Name, mod);
        }

        foreach (var duplicate in Mods.Where(m => !ReferenceEquals(existing[m.Name], m)).ToList())
        {
            Mods.Remove(duplicate);
        }

        var wanted = new List<ModEntryViewModel>();
        foreach (var info in scanned)
        {
            if (existing.Remove(info.Name, out var entry))
            {
                entry.Refresh(info);
            }
            else
            {
                entry = CreateEntry(info);
            }

            wanted.Add(entry);
        }

        foreach (var gone in existing.Values)
        {
            Mods.Remove(gone);
            if (ReferenceEquals(gone, _selectionAnchor))
            {
                _selectionAnchor = null;
            }
        }

        for (var target = 0; target < wanted.Count; target++)
        {
            var current = Mods.IndexOf(wanted[target]);
            if (current < 0)
            {
                Mods.Insert(target, wanted[target]);
            }
            else if (current != target)
            {
                Mods.Move(current, target);
            }
        }

        UpdateMoveFlags();
        Raise(nameof(HasSelection));
        Raise(nameof(HasMods));
        Raise(nameof(ModsSummary));
        Raise(nameof(EnabledModCount));
    }

    private ModEntryViewModel CreateEntry(ModInfo info)
    {
        ModEntryViewModel entry = null!;
        entry = new ModEntryViewModel(info, onToggled: () => OnModToggled(entry));
        return entry;
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
        if (ModScanner.SetEnabledOnDisk(entry.Info, entry.Enabled) is { } renamed)
        {
            entry.Refresh(renamed);
            LoadOrderError = "";
            _modsSignature = string.Join("\n", Mods.Select(m => $"{m.Name}|{m.Enabled}|{m.Required}"));
        }
        else
        {
            LoadOrderError = $"Could not rename {entry.Name} in the mods folder.";
            entry.Refresh(entry.Info);
        }

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
