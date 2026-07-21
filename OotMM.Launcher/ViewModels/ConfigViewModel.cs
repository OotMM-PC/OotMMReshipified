using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// The pre-launch configuration form: seed, game builds, mods, multiworld.
/// State mirrors the original launcher's ConfigForm; the session coordinator
/// that consumes it (Setup flow, process spawn) is a later module, so the big
/// button currently reports what it *will* do instead of doing it.
/// </summary>
public sealed class ConfigViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private readonly Action _previewRuntime;
    private readonly Action<Seed, string, string, string> _startSession;
    private readonly DispatcherTimer _rescanTimer;

    private string _seedPath = "";
    private Seed? _seed;
    private string _seedError = "";
    private bool _hostRelay;
    private int _relayPort;
    private bool _hostPvp;
    private string _multiworldAddress = "";
    private string _playerName = "";
    private string _statusMessage = "";
    private bool _isActive = true;
    private bool _restoring;

    public ConfigViewModel(UiSettings settings, IFilePicker picker, Action previewRuntime,
                           Action<Seed, string, string, string> startSession)
    {
        _settings = settings;
        _previewRuntime = previewRuntime;
        _startSession = startSession;

        OotCard = new GameCardViewModel(isOot: true, picker, FormChanged);
        MmCard = new GameCardViewModel(isOot: false, picker, FormChanged);

        BrowseSeedCommand = new RelayCommand(() => _ = BrowseSeedAsync(picker));
        LaunchCommand = new RelayCommand(OnLaunchClicked);
        PreviewRuntimeCommand = new RelayCommand(_previewRuntime);

        // Restore the last session's form. Saving is suppressed until every field
        // is back: the first card's change callback would otherwise persist the
        // not-yet-restored fields as empty, wiping them.
        _restoring = true;
        _seedPath = settings.SeedPath;
        _hostRelay = settings.HostRelay;
        _relayPort = settings.RelayPort;
        _hostPvp = settings.EnablePvp;
        _multiworldAddress = settings.MultiworldAddress;
        _playerName = settings.PlayerName;
        OotCard.ExePath = settings.OotExePath;
        MmCard.ExePath = settings.MmExePath;

        RefreshSeedPreview();
        _restoring = false;

        // Periodic rescan so archives dropped into mods/ while the launcher is
        // open show up (and selections pointing at removed files fall out)
        // without a restart — same 2s cadence as the original.
        _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _rescanTimer.Tick += (_, _) =>
        {
            if (_isActive)
            {
                RescanAll();
            }
        };
        _rescanTimer.Start();
    }

    public GameCardViewModel OotCard { get; }
    public GameCardViewModel MmCard { get; }

    public RelayCommand BrowseSeedCommand { get; }
    public RelayCommand LaunchCommand { get; }
    public RelayCommand PreviewRuntimeCommand { get; }

    public ObservableCollection<string> ValidationErrors { get; } = [];
    public ObservableCollection<ModelPickViewModel> OotModelPicks { get; } = [];
    public ObservableCollection<ModelPickViewModel> MmModelPicks { get; } = [];

    public string OotModelSummary => ModelSummary("OoT player model", OotModelPicks);
    public string MmModelSummary => ModelSummary("MM player model", MmModelPicks);

    private static string ModelSummary(string label, ObservableCollection<ModelPickViewModel> picks)
    {
        var count = picks.Count(p => p.IsSelected);
        return $"{label} ({count} archive{(count == 1 ? "" : "s")} selected)";
    }

    /// <summary>Paused while the runtime screen is shown so the timer does not
    /// churn the disk behind an active session.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    // --- Seed ---------------------------------------------------------------

    public string SeedPath
    {
        get => _seedPath;
        set
        {
            if (Set(ref _seedPath, value))
            {
                RefreshSeedPreview();
                FormChanged();
            }
        }
    }

    public Seed? Seed
    {
        get => _seed;
        private set
        {
            _seed = value;
            Raise();
            Raise(nameof(SeedSummary));
            Raise(nameof(HasSeed));
            Raise(nameof(ShowMultiworld));
            Raise(nameof(PlayerSlotText));
        }
    }

    public bool HasSeed => Seed is not null;

    public string SeedError
    {
        get => _seedError;
        private set
        {
            if (Set(ref _seedError, value))
            {
                Raise(nameof(HasSeedError));
            }
        }
    }

    public bool HasSeedError => SeedError.Length > 0;

    public string SeedSummary =>
        Seed is null
            ? ""
            : $"{Seed.Placements.Count} checks — begins in {GameInfo.Title(Seed.ResolveStartingGame())}"
              + (Seed.IsMultiworld ? " — MULTIWORLD" : "");

    // --- Multiworld ----------------------------------------------------------

    public bool ShowMultiworld => Seed?.IsMultiworld == true;

    public string PlayerSlotText =>
        Seed is null ? "" : $"You are player {Seed.PlayerId} of {Seed.PlayerCount}.";

    public bool HostRelay
    {
        get => _hostRelay;
        set
        {
            if (Set(ref _hostRelay, value))
            {
                FormChanged();
            }
        }
    }

    public int RelayPort
    {
        get => _relayPort;
        set
        {
            // Same clamp as the original: any TCP port, silently corrected.
            var clamped = Math.Clamp(value, 1, 65535);
            if (Set(ref _relayPort, clamped))
            {
                FormChanged();
            }
        }
    }

    public bool HostPvp
    {
        get => _hostPvp;
        set
        {
            if (Set(ref _hostPvp, value))
            {
                FormChanged();
            }
        }
    }

    public string MultiworldAddress
    {
        get => _multiworldAddress;
        set
        {
            if (Set(ref _multiworldAddress, value))
            {
                FormChanged();
            }
        }
    }

    public string PlayerName
    {
        get => _playerName;
        set
        {
            if (Set(ref _playerName, value))
            {
                FormChanged();
            }
        }
    }

    // --- Launch button -------------------------------------------------------

    public bool SetupNeeded =>
        OotCard.MissingArchives.Count > 0
        || MmCard.MissingArchives.Count > 0
        || CrossGameModelModsMissing;

    /// <summary>The launcher-built cross-game item-model archives; without them
    /// foreign items render as glitched placeholders, so Setup builds them.</summary>
    private bool CrossGameModelModsMissing =>
        (OotCard.ExeExists && OotCard.Mods.All(m => m.Name != "ootmm_mm_models.o2r"))
        || (MmCard.ExeExists && MmCard.Mods.All(m => m.Name != "ootmm_oot_models.o2r"));

    public string LaunchButtonText => SetupNeeded ? "SETUP" : "START ADVENTURE";

    public bool ShowSetupHint => SetupNeeded;

    public bool ShowMqHint => SetupNeeded && (Seed?.NeedsMasterQuest() ?? false);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    // --- Internals -----------------------------------------------------------

    private async Task BrowseSeedAsync(IFilePicker picker)
    {
        var picked = await picker.PickFileAsync("Select an OoTMM seed", "OoTMM seed", "*.json");
        if (picked is not null)
        {
            SeedPath = picked;
        }
    }

    private void RefreshSeedPreview()
    {
        if (string.IsNullOrWhiteSpace(SeedPath))
        {
            Seed = null;
            SeedError = "";
        }
        else if (!File.Exists(SeedPath))
        {
            Seed = null;
            SeedError = "Seed file not found at this path.";
        }
        else
        {
            try
            {
                Seed = SeedLoader.LoadFromFile(SeedPath);
                SeedError = "";
            }
            catch (SeedLoadException ex)
            {
                Seed = null;
                SeedError = ex.Message;
            }
        }

        // An MQ seed changes which archives OoT needs, so setup state follows
        // the seed, not just the exe paths.
        RefreshDerivedState();
    }

    /// <summary>Any form change funnels here: revalidate, refresh setup state,
    /// rebuild model picks, persist.</summary>
    private void FormChanged()
    {
        RefreshDerivedState();
        if (!_restoring)
        {
            SaveToSettings();
        }
    }

    private void RescanAll()
    {
        OotCard.RefreshMods();
        MmCard.RefreshMods();
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        var needsMq = Seed?.NeedsMasterQuest() ?? false;
        OotCard.RefreshSetupState(needsMq);
        MmCard.RefreshSetupState(seedNeedsMq: false);

        RebuildModelPicks(OotModelPicks, OotCard, _settings.PlayerModelOot);
        RebuildModelPicks(MmModelPicks, MmCard, _settings.PlayerModelMm);

        Revalidate();
        Raise(nameof(SetupNeeded));
        Raise(nameof(LaunchButtonText));
        Raise(nameof(ShowSetupHint));
        Raise(nameof(ShowMqHint));
        Raise(nameof(OotModelSummary));
        Raise(nameof(MmModelSummary));
    }

    /// <summary>Keeps each pick list in sync with the scanned mods while
    /// preserving selections; a stale selection (archive renamed or removed)
    /// silently falls out, matching the original launcher.</summary>
    private void RebuildModelPicks(
        ObservableCollection<ModelPickViewModel> picks, GameCardViewModel card, List<string> saved)
    {
        var selected = picks.Where(p => p.IsSelected).Select(p => p.Name)
            .Concat(saved)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wanted = card.Mods.Where(m => !m.Required).Select(m => m.Name).ToList();
        if (wanted.Count == 0 && picks.Count > 0)
        {
            return; // transient exe/drive hiccup must not wipe the pick list
        }

        if (wanted.SequenceEqual(picks.Select(p => p.Name), StringComparer.OrdinalIgnoreCase))
        {
            return; // no churn: rebuilding on every tick would fight open dropdowns
        }

        picks.Clear();
        foreach (var name in wanted)
        {
            picks.Add(new ModelPickViewModel(name, selected.Contains(name), FormChanged));
        }
    }

    private void Revalidate()
    {
        ValidationErrors.Clear();

        if (OotCard.ExePath.Length > 0 && !OotCard.ExeExists)
        {
            ValidationErrors.Add("The Ocarina of Time executable was not found at its configured path.");
        }

        if (MmCard.ExePath.Length > 0 && !MmCard.ExeExists)
        {
            ValidationErrors.Add("The Majora's Mask executable was not found at its configured path.");
        }

        if (OotCard.ExeExists && OotCard.Mods.All(m => m.Name != "ootmm_mm_models.o2r"))
        {
            ValidationErrors.Add(
                "OoT mods folder is missing ootmm_mm_models.o2r (cross-game item models) — run Setup to build it.");
        }

        if (MmCard.ExeExists && MmCard.Mods.All(m => m.Name != "ootmm_oot_models.o2r"))
        {
            ValidationErrors.Add(
                "MM mods folder is missing ootmm_oot_models.o2r (cross-game item models) — run Setup to build it.");
        }

        if (ShowMultiworld)
        {
            if (string.IsNullOrWhiteSpace(PlayerName))
            {
                ValidationErrors.Add("Enter a player name for the multiworld session.");
            }

            if (!HostRelay && !IsValidHostPort(MultiworldAddress))
            {
                ValidationErrors.Add("Enter the relay server address as host:port.");
            }
        }
    }

    private static bool IsValidHostPort(string address)
    {
        var colon = address.LastIndexOf(':');
        if (colon <= 0 || colon == address.Length - 1)
        {
            return false;
        }

        return int.TryParse(address[(colon + 1)..], out var port) && port is >= 1 and <= 65535;
    }

    private void OnLaunchClicked()
    {
        if (Seed is not { } seed)
        {
            StatusMessage = "Choose a seed first.";
            return;
        }

        if (!OotCard.ExeExists || !MmCard.ExeExists)
        {
            StatusMessage = "Both game executables must be set before starting.";
            return;
        }

        if (ValidationErrors.Count > 0)
        {
            StatusMessage = "Fix the issues listed above first.";
            return;
        }

        if (SetupNeeded)
        {
            // The Setup flow (ROM extraction + model archive build) is a later slice.
            StatusMessage = "Setup will extract game assets and build the cross-game model archives once the setup flow is ported.";
            return;
        }

        StatusMessage = "";
        _startSession(seed, SeedPath, OotCard.ExePath, MmCard.ExePath);
    }

    public void ShowStatus(string message) => StatusMessage = message;

    private void SaveToSettings()
    {
        _settings.SeedPath = SeedPath;
        _settings.OotExePath = OotCard.ExePath;
        _settings.MmExePath = MmCard.ExePath;
        _settings.HostRelay = HostRelay;
        _settings.RelayPort = RelayPort;
        _settings.EnablePvp = HostPvp;
        _settings.MultiworldAddress = MultiworldAddress;
        _settings.PlayerName = PlayerName;
        // An empty pick collection means "no scan results", never "deselect all";
        // persisting it would wipe the saved cosmetics.
        if (OotModelPicks.Count > 0)
        {
            _settings.PlayerModelOot = OotModelPicks.Where(p => p.IsSelected).Select(p => p.Name).ToList();
        }

        if (MmModelPicks.Count > 0)
        {
            _settings.PlayerModelMm = MmModelPicks.Where(p => p.IsSelected).Select(p => p.Name).ToList();
        }

        _settings.Save();
    }
}
