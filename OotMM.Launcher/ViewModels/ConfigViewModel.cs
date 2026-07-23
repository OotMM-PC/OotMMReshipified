using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.ViewModels;

/// <summary>
/// The pre-launch configuration form: seed, game builds, mods, multiworld.
/// State mirrors the original launcher's ConfigForm and drives first-run game
/// extraction before handing a ready configuration to the session coordinator.
/// </summary>
public sealed class ConfigViewModel : ViewModelBase
{
    private readonly UiSettings _settings;
    private readonly Action _previewRuntime;
    private readonly Action<Seed, string, string, string> _startSession;
    private readonly DispatcherTimer _rescanTimer;
    private readonly DispatcherTimer _setupTimer;
    private readonly Queue<(string ExePath, bool IsOot, bool NeedMq)> _setupQueue = [];

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
    private GameProcess? _setupProcess;
    private Task<OotmmAssetBuildResult>? _assetBuildTask;

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

        _setupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _setupTimer.Tick += (_, _) => TickSetup();
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
        || GeneratedAssetsNeedBuild;

    public bool IsSetupInProgress =>
        _setupProcess is not null || _setupQueue.Count > 0 || _assetBuildTask is not null;

    private bool GeneratedAssetsNeedBuild =>
        OotmmAssetBuilder.NeedsBuild(OotCard.ExePath, isOot: true)
        || OotmmAssetBuilder.NeedsBuild(MmCard.ExePath, isOot: false);

    public string LaunchButtonText => IsSetupInProgress
        ? "SETUP IN PROGRESS..."
        : SetupNeeded ? "SETUP" : "START ADVENTURE";

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
        Raise(nameof(IsSetupInProgress));
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

        if (OotCard.ExeExists && OotCard.MissingArchives.Count == 0
                              && OotCard.Mods.All(m => m.Name != OotmmAssetBuilder.ArchiveName))
        {
            ValidationErrors.Add(
                $"OoT mods folder is missing {OotmmAssetBuilder.ArchiveName} — run Setup to build it.");
        }

        if (MmCard.ExeExists && MmCard.MissingArchives.Count == 0
                             && MmCard.Mods.All(m => m.Name != OotmmAssetBuilder.ArchiveName))
        {
            ValidationErrors.Add(
                $"MM mods folder is missing {OotmmAssetBuilder.ArchiveName} — run Setup to build it.");
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
        if (IsSetupInProgress)
        {
            return;
        }

        if (SetupNeeded)
        {
            BeginSetup(Seed?.NeedsMasterQuest() ?? false);
            return;
        }

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

        StatusMessage = "";
        _startSession(seed, SeedPath, OotCard.ExePath, MmCard.ExePath);
    }

    private void BeginSetup(bool needMq)
    {
        RescanAll();
        _setupQueue.Clear();

        if (OotCard.MissingArchives.Any(name => name is "oot.o2r" or "oot-mq.o2r"))
        {
            _setupQueue.Enqueue((OotCard.ExePath, true, needMq));
        }

        if (MmCard.MissingArchives.Contains("mm.o2r"))
        {
            _setupQueue.Enqueue((MmCard.ExePath, false, false));
        }

        if (_setupQueue.Count == 0)
        {
            StartAssetBuildOrComplete();
            return;
        }

        _setupTimer.Start();
        StartNextSetupGame();
    }

    private void TickSetup()
    {
        if (_assetBuildTask is not null)
        {
            if (!_assetBuildTask.IsCompleted)
            {
                return;
            }

            FinishAssetBuild();
            return;
        }

        if (_setupProcess is { Running: true })
        {
            return;
        }

        _setupProcess?.Dispose();
        _setupProcess = null;
        RescanAll();

        if (_setupQueue.Count > 0)
        {
            StartNextSetupGame();
            return;
        }

        StartAssetBuildOrComplete();
    }

    private void StartNextSetupGame()
    {
        var next = _setupQueue.Dequeue();
        var gameTitle = next.IsOot ? "Ocarina of Time" : "Majora's Mask";
        _setupProcess = new GameProcess(next.ExePath, "", setupOnly: true, setupNeedMq: next.NeedMq);
        if (!_setupProcess.Valid)
        {
            _setupProcess.Dispose();
            _setupProcess = null;
            _setupQueue.Clear();
            _setupTimer.Stop();
            StatusMessage = $"Setup failed to start {gameTitle}.";
        }
        else
        {
            StatusMessage = $"Setup: {gameTitle} is open — follow its prompts to select your ROM.";
        }

        Raise(nameof(IsSetupInProgress));
        Raise(nameof(LaunchButtonText));
    }

    private void StartAssetBuildOrComplete()
    {
        var archivesMissing = OotCard.MissingArchives.Count > 0 || MmCard.MissingArchives.Count > 0;
        if (archivesMissing || !GeneratedAssetsNeedBuild)
        {
            CompleteSetup();
            return;
        }

        StatusMessage = "Setup: building OoTMM gameplay assets...";
        _assetBuildTask = Task.Run(() => OotmmAssetBuilder.Build(OotCard.ExePath, MmCard.ExePath));
        _setupTimer.Start();
        Raise(nameof(IsSetupInProgress));
        Raise(nameof(LaunchButtonText));
    }

    private void FinishAssetBuild()
    {
        OotmmAssetBuildResult result;
        try
        {
            result = _assetBuildTask!.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            result = new OotmmAssetBuildResult(false, 0, [], ex.Message);
        }

        _assetBuildTask = null;
        RescanAll();
        StatusMessage = result.Ok
            ? $"Setup complete — generated {result.Entries} OoTMM asset entries."
            : $"Setup failed while building OoTMM assets — {result.Error}";
        _setupTimer.Stop();
        Raise(nameof(IsSetupInProgress));
        Raise(nameof(LaunchButtonText));
    }

    private void CompleteSetup()
    {
        _setupTimer.Stop();
        var missingRomArchives = OotCard.MissingArchives
            .Where(name => name is "oot.o2r" or "oot-mq.o2r")
            .Concat(MmCard.MissingArchives.Where(name => name == "mm.o2r"))
            .ToList();
        var missingPortArchives = OotCard.MissingArchives
            .Where(name => name == "soh.o2r")
            .Concat(MmCard.MissingArchives.Where(name => name == "2ship.o2r"))
            .ToList();

        if (missingRomArchives.Count > 0)
        {
            StatusMessage = $"Setup did not finish — still missing {string.Join(", ", missingRomArchives)}.";
        }
        else if (missingPortArchives.Count > 0)
        {
            StatusMessage =
                $"Copy the separately built {string.Join(" and ", missingPortArchives)} next to the game executables.";
        }
        else if (GeneratedAssetsNeedBuild)
        {
            StatusMessage = "Game extraction is complete. OoTMM gameplay assets still need to be built.";
        }
        else
        {
            StatusMessage = "All game assets are ready.";
        }

        Raise(nameof(IsSetupInProgress));
        Raise(nameof(LaunchButtonText));
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
