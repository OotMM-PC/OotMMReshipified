using OotMM.Core;
using OotMM.Launcher.Models;
using OotMM.Launcher.Services;

namespace OotMM.Launcher.Session;

public sealed class SessionCoordinator : IDisposable
{
    private const int HandoffGraceMs = 400;
    private const uint FaroresWindCrossEntrance = 0xFFFD;
    private const uint DungeonRespawnEntrance = 0xFFFC;

    private readonly Core.Session _session;
    private readonly UiTimer _timer;
    private readonly UiTimer _focusTimer;
    private readonly UiTimer _presenceTimer;
    private readonly MultiworldSync? _multiworld;
    private readonly ModelSyncCoordinator? _modelSync;
    private readonly SessionInventory _inventory;
    private bool _leftButtonWasDown;
    private GameProcess? _process;
    private GameIpcServer? _ipc;
    private nint _gameChild;
    private (int Width, int Height) _laidOutSize;
    private long _outboxOffset;
    private CrossGameTransition? _pendingTransition;
    private DateTime _handoffDeadline;
    private uint? _launchEntrance;
    private uint? _launchOotAge;
    private DateTime _launchedAt;
    private int _launchRetries;
    private bool _relaunching;
    private PendingSwitch? _pendingSwitch;
    private ulong _saveRequestCounter;
    private DateTime _stateStamp = DateTime.MinValue;
    private bool _stateDirty;
    private TrapTuning _traps = new();
    private bool _initialSyncDone;
    private bool _ended;
    private bool _recoveryPending;
    private bool _starting;
    private CancellationTokenSource? _startupCancellation;
    private int _speedPercent = 100;
    private bool _speedSmooth;
    private bool _zTargetHold;
    private bool _fairySpotPads;
    private bool _potRolling = true;
    private bool _steadyDeityBeams = true;
    private bool _ipcSettingsDirty = true;
    private readonly List<(ushort SourcePlayer, ItemRef Item)> _pendingRemoteItems = [];
    private readonly List<(Game Game, string Path, int Player)> _pendingLiveModels = [];
    private long _settingsRevision;
    private ulong _lastNativeSaveRequest;
    private readonly Dictionary<string, ItemRef> _resolvedCheckRewards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _endgameBosses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _renewalGrants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _discoveredEntrances = new(StringComparer.Ordinal);
    private readonly EntranceMap? _entranceMap;

    public SessionCoordinator(Core.Session session, MultiworldSync? multiworld = null,
                              ModelSyncCoordinator? modelSync = null, Game? startingGame = null)
    {
        _session = session;
        _multiworld = multiworld;
        _modelSync = modelSync;
        _inventory = new SessionInventory(session.Seed, session.DebugItemsPath);
        _entranceMap = EntranceMapPack.LoadInstalled() is { } pack ? new EntranceMap(session.Seed, pack) : null;
        _zTargetHold = session.Seed.Cosmetics.DefaultHold;
        ActiveGame = session.RecoveredTransition?.ToGame ?? startingGame ?? session.Seed.ResolveStartingGame();
        _timer = UiScheduler.CreateTimer(TimeSpan.FromMilliseconds(250), Tick);
        _focusTimer = UiScheduler.CreateTimer(TimeSpan.FromMilliseconds(25), PollGameFocus);
        _presenceTimer = UiScheduler.CreateTimer(TimeSpan.FromMilliseconds(50), PumpPresence);

        if (_multiworld is not null)
        {
            _multiworld.EventLogged += (message, isRemote) =>
            {
                if (isRemote)
                {
                    RemoteEventLogged?.Invoke(message);
                }
                else
                {
                    LogEvent(message);
                }
            };
            _multiworld.OwnFactConfirmed += OnOwnFactConfirmed;
            _multiworld.RemoteItemReceived += OnRemoteItemReceived;
            _multiworld.SyncChanged += () =>
            {
                _stateDirty = true;
                _ipcSettingsDirty = true;
            };
            _multiworld.RulesChanged += () =>
            {
                SpeedPercent = _speedPercent;
                _ipcSettingsDirty = true;
                StateChanged?.Invoke();
            };
            if (_modelSync is not null)
            {
                _multiworld.ModelLineReceived += _modelSync.HandleLine;
                _modelSync.Logged += LogEvent;
                _modelSync.LiveArchiveReady += (game, path, player) =>
                {
                    _pendingLiveModels.Add((game, path, player));
                    FlushLiveModels();
                };
            }
        }
    }

    public Core.Session Session => _session;

    public MultiworldSync? Multiworld => _multiworld;

    public Game ActiveGame { get; private set; }

    public bool IsRunning => _process?.Running == true;

    public nint GameHostHandle { get; set; }

    public void OpenInGameMenu() => GameWindow.PostEscape(_gameChild);

    public bool TimerEnabled => _multiworld?.TimerEnabled ?? true;

    public int TimerMin => Math.Max(0, Math.Min(_multiworld?.TimerMin ?? 0, TimerMax));

    public int TimerMax => Math.Max(0, _multiworld?.TimerMax ?? 250);

    public int SpeedPercent
    {
        get => _speedPercent;
        set
        {
            var clamped = Math.Clamp(value, TimerMin, Math.Max(TimerMin, TimerMax));
            if (_speedPercent != clamped)
            {
                _speedPercent = clamped;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool SpeedSmooth
    {
        get => _speedSmooth;
        set
        {
            if (_speedSmooth != value)
            {
                _speedSmooth = value;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool RemoteItemPrompts { get; set; } = true;

    public TrapTuning Traps
    {
        get => _multiworld?.Traps ?? _traps;
        set
        {
            _traps = (value ?? new TrapTuning()).Normalized();
            _ipcSettingsDirty = true;
        }
    }

    public bool ZTargetHold
    {
        get => _zTargetHold;
        set
        {
            if (_zTargetHold != value)
            {
                _zTargetHold = value;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool FairySpotPads
    {
        get => _fairySpotPads;
        set
        {
            if (_fairySpotPads != value)
            {
                _fairySpotPads = value;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool PotRolling
    {
        get => _potRolling;
        set
        {
            if (_potRolling != value)
            {
                _potRolling = value;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool SteadyDeityBeams
    {
        get => _steadyDeityBeams;
        set
        {
            if (_steadyDeityBeams != value)
            {
                _steadyDeityBeams = value;
                _ipcSettingsDirty = true;
            }
        }
    }

    public bool AllowDebugMenus
    {
        get => _session.AllowDebugMenus;
        set => _session.AllowDebugMenus = value;
    }

    public bool ForceUnlock(string checkKey)
    {
        if (_ended || _pendingTransition is not null || ResolveLocalCheck(checkKey) is not { } placement)
        {
            return false;
        }

        var key = Identity.CheckKey(placement.Check.Game, placement.Check.Id);
        if (CompletedChecks.Contains(key))
        {
            return false;
        }

        HandleCheckCollected(key);
        if (!CompletedChecks.Contains(key))
        {
            LogEvent($"Force-unlock failed for {placement.Check.Name}.");
            return false;
        }

        ReconcileInventory();
        LogEvent($"Force-unlocked {placement.Check.Name}.");
        return true;
    }

    public HashSet<string> CompletedChecks { get; } = [];

    public IReadOnlyCollection<string> EndgameBosses => _endgameBosses;

    /// <summary>The seed's entrance map, or null when no map pictures are installed.</summary>
    public EntranceMap? EntranceMap => _entranceMap;

    public MapPosition? MapPosition => _entranceMap?.Position;

    public int EntranceRevision { get; private set; }

    public event Action? StateChanged;
    public event Action<string>? EventLogged;
    public event Action<string>? RemoteEventLogged;
    public event Action<string>? Ended;
    public event Action<Core.Session, Game>? SaveSelectionReady;

    public string? Start()
    {
        if (!_inventory.IsValid)
        {
            return _inventory.LastError;
        }

        uint? entrance = null;
        uint? ootAge = null;
        if (_session.RecoveredTransition is { } recovered)
        {
            ActiveGame = recovered.ToGame;
            entrance = recovered.ToNativeId;
            ootAge = recovered.OotAge;
        }

        SyncCompletedFromState(force: true);
        ReconcileInventory();
        var error = StartActiveGame(entrance, ootAge);
        error = string.IsNullOrEmpty(error) ? null : error;
        if (error is null && !_ended)
        {
            _timer.Start();
            _focusTimer.Start();
            _presenceTimer.Start();
        }

        return error;
    }

    public void RestartGame()
    {
        _process?.Terminate();
        _modelSync?.InstallNow();
        LogEvent($"Restarting {GameInfo.Title(ActiveGame)}…");
        if (StartActiveGame(null, null) is { } error)
            EndSession(error);
    }

    public void RestartToOotTitle()
    {
        _process?.Terminate();
        _modelSync?.InstallNow();
        ActiveGame = Game.Oot;
        LogEvent("Restarting to the Ocarina of Time title screen…");
        if (StartActiveGame(null, null) is { } error)
            EndSession(error);
    }

    public static readonly TimeSpan SwitchSaveTimeout = TimeSpan.FromSeconds(10);

    public bool IsSwitchPending => _pendingSwitch is not null;

    public void SwitchGame(uint? ootAge = null)
    {
        if (_ended || _starting || _pendingSwitch is not null)
            return;
        if (_process?.Running == true && _ipc?.IsConnected == true)
        {
            var requestId = ++_saveRequestCounter;
            if (_ipc.Send(GameIpcProtocol.BuildSaveRequest(requestId)))
            {
                _pendingSwitch = new PendingSwitch(requestId, DateTime.UtcNow + SwitchSaveTimeout, ootAge);
                LogEvent($"Saving {GameInfo.Title(ActiveGame)} before switching…");
                StateChanged?.Invoke();
                return;
            }
        }
        PerformSwitch(ootAge);
    }

    private void CompletePendingSwitch(ulong requestId, bool saved)
    {
        if (_pendingSwitch is not { } pending || pending.RequestId != requestId)
            return;
        _pendingSwitch = null;
        if (saved)
        {
            PerformSwitch(pending.OotAge);
            return;
        }
        LogEvent($"{GameInfo.Title(ActiveGame)} could not save, so the switch was cancelled. Try again.");
        StateChanged?.Invoke();
    }

    private void ExpirePendingSwitch()
    {
        if (_pendingSwitch is not { } pending || DateTime.UtcNow < pending.Deadline)
            return;
        _pendingSwitch = null;
        LogEvent($"{GameInfo.Title(ActiveGame)} did not confirm its save, so the switch was cancelled. Try again.");
        StateChanged?.Invoke();
    }

    private void PerformSwitch(uint? ootAge)
    {
        _process?.Terminate();
        _modelSync?.InstallNow();
        ActiveGame = ActiveGame == Game.Oot ? Game.Mm : Game.Oot;
        LogEvent($"Switching to {GameInfo.Title(ActiveGame)}…");
        if (StartActiveGame(null, ActiveGame == Game.Mm ? ootAge : null) is { } error)
            EndSession(error);
    }

    public void EndSession(string reason)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _startupCancellation?.Cancel();
        _timer.Stop();
        _focusTimer.Stop();
        _presenceTimer.Stop();
        _process?.Terminate();
        _process?.Dispose();
        _process = null;
        _ipc?.Dispose();
        _ipc = null;
        _modelSync?.InstallNow();
        _multiworld?.Dispose();
        Ended?.Invoke(reason);
    }

    public bool ResetSeedProgress()
    {
        if (_session.Seed.IsMultiworld)
        {
            LogEvent("Reset refused: multiworld progress requires an offline epoch reset.");
            return false;
        }

        _startupCancellation?.Cancel();
        _process?.Terminate();
        var ok = true;
        try
        {
            File.Delete(_session.StatePath);
            File.Delete(_session.DebugItemsPath);
            for (var slot = 0; slot < 3; slot++)
            {
                File.Delete($"{_session.StatePath}.slot{slot}");
            }

            var stateName = Path.GetFileName(_session.StatePath);
            foreach (var sidecar in Directory.EnumerateFiles(_session.SessionDir))
            {
                var name = Path.GetFileName(sidecar);
                if (name.StartsWith(stateName + ".applied", StringComparison.Ordinal)
                    || name.StartsWith(stateName + ".triforce", StringComparison.Ordinal)
                    || name.StartsWith(stateName + ".vitals", StringComparison.Ordinal))
                {
                    File.Delete(sidecar);
                }
            }

            foreach (var path in new[]
                     {
                         _session.OotInbox, _session.OotOutbox, _session.MmInbox, _session.MmOutbox,
                     })
            {
                ok &= DurableFile.TruncateDurably(path);
            }

            ok &= DurableFile.WriteOffset(_session.OotOutboxCursor, 0);
            ok &= DurableFile.WriteOffset(_session.MmOutboxCursor, 0);

            var tag = SessionPaths.SaveTag(_session.Seed);
            foreach (var dir in new[]
                     {
                         Path.Combine(SessionPaths.OotNativeSavesRoot(_session), "ootmm-" + tag),
                         Path.Combine(SessionPaths.MmNativeSavesRoot(_session), "ootmm-" + tag),
                     })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ok = false;
        }

        CompletedChecks.Clear();
        _resolvedCheckRewards.Clear();
        _endgameBosses.Clear();
        _renewalGrants.Clear();
        _discoveredEntrances.Clear();
        _entranceMap?.Reset();
        EntranceRevision++;
        ok &= _inventory.TryClearDebugAdjustments();
        ReconcileInventory();
        _stateStamp = DateTime.MinValue;
        LogEvent(ok ? "Seed progress reset." : "Seed progress reset finished with errors.");
        StateChanged?.Invoke();
        StartActiveGame(null, null);
        return ok;
    }

    public void Dispose()
    {
        _ended = true;
        _startupCancellation?.Cancel();
        _timer.Dispose();
        _focusTimer.Dispose();
        _presenceTimer.Dispose();
        _process?.Terminate();
        _process?.Dispose();
        _ipc?.Dispose();
        _modelSync?.InstallNow();
        _multiworld?.Dispose();
    }

    private string? StartActiveGame(uint? bootEntrance, uint? ootAge)
    {
        if (_ended) return "This session has ended.";
        if (_session.RecoveredTransition is { } recovered && recovered.ToGame == ActiveGame)
        {
            bootEntrance ??= recovered.ToNativeId;
            ootAge ??= recovered.OotAge;
        }
        _startupCancellation?.Cancel();
        var startup = new CancellationTokenSource();
        _startupCancellation = startup;
        _starting = true;
        _ = CompleteStartupAsync(bootEntrance, ootAge, startup);
        return null;
    }

    private async Task CompleteStartupAsync(uint? bootEntrance, uint? ootAge, CancellationTokenSource startup)
    {
        try
        {
            var error = await PrepareAndStartGameAsync(bootEntrance, ootAge, startup.Token);
            if (!startup.IsCancellationRequested && error is not null) EndSession(error);
        }
        catch (OperationCanceledException) when (startup.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            if (!startup.IsCancellationRequested) EndSession("Could not start the game: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_startupCancellation, startup))
            {
                _startupCancellation = null;
                _starting = false;
                if (!_ended) StateChanged?.Invoke();
            }
            startup.Dispose();
        }
    }

    private async Task<string?> PrepareAndStartGameAsync(uint? bootEntrance, uint? ootAge, CancellationToken token)
    {
        var exe = ActiveGame == Game.Oot ? _session.OotExe : _session.MmExe;
        var game = ActiveGame;
        var compatibilityError = await Task.Run(() => NativeBuildCompatibility.Validate(exe, game, out var error)
            ? null : error, token);
        token.ThrowIfCancellationRequested();
        if (compatibilityError is not null) return compatibilityError;
        var boot = SessionPaths.MakeBootConfig(_session, ActiveGame, bootEntrance, ootAge);
        boot.OpenFileSelect = _session.OpenFileSelect;
        MmSaveSource? selectedMm = null;
        if ((ActiveGame == Game.Mm || SharedResourceFamilies.AnyEnabled(_session.Seed))
            && _session.LogicalSlot is { } slot)
        {
            var primary = Path.Combine(SessionPaths.MmNativeSavesRoot(_session),
                "ootmm-" + SessionPaths.SaveTag(_session.Seed), $"file{slot + 1}.json");
            if (File.Exists(primary + NativeMmSaveValidation.SavePairJournalSuffix))
                LogEvent("Recovering an interrupted MM save…");
            var pairError = await NativeMmSaveValidation.RecoverPairAsync(_session.MmExe, primary,
                _session.SaveId, slot, token);
            token.ThrowIfCancellationRequested();
            if (pairError is not null) return pairError;
            if (!MmSaveSource.TrySelect(primary, _session.SaveId, slot, out var source, out var recoveryError))
                return recoveryError;
            selectedMm = source;
            if (source is not null)
            {
                LogEvent("Validating the MM save…");
                var validationError = await NativeMmSaveValidation.ValidateAsync(_session.MmExe, source,
                    _session.SaveId, slot, token);
                token.ThrowIfCancellationRequested();
                if (validationError is not null) return validationError;
            }
            if (ActiveGame == Game.Mm && source?.FromBackup == true)
                boot.MmRecovery = new MmRecoveryRequest(source.Contents, source.DamagedPrimaryHex);
        }
        if (!DurableFile.Retire(_session.StatePath + ".recovery-result.json"))
            return "Could not clear the previous save recovery result.";
        if (!SharedResourceSaves.TryRead(_session, out var sharedResources, out var sharedError, selectedMm, true))
            return sharedError;
        boot.SharedResources = sharedResources;
        if (!DurableFile.WriteAtomic(_session.BootConfigPath, boot.Serialize()))
        {
            return $"cannot durably publish boot configuration {_session.BootConfigPath}";
        }
        _recoveryPending = boot.MmRecovery is not null;

        if (!_session.Seed.IsMultiworld)
        {
            File.WriteAllText(ActiveOutbox, "");
            DurableFile.WriteOffset(ActiveOutboxCursor, 0);
        }

        GameAudioSync.PrepareLaunch(_session.OotExe, _session.MmExe, ActiveGame);
        _modelSync?.NoteGameLaunching(ActiveGame);
        _process?.Dispose();
        _ipc?.Dispose();
        _ipc = new GameIpcServer(ActiveGame);
        _gameChild = 0;
        _process = new GameProcess(
            exe,
            _session.BootConfigPath,
            parentWindow: GameHostHandle,
            ipcEndpoint: _ipc.Endpoint,
            ipcToken: _ipc.Token);
        if (!_process.Valid)
        {
            _process = null;
            _ipc.Dispose();
            _ipc = null;
            StateChanged?.Invoke();
            return $"could not start {exe}";
        }

        if (_session.RecoveredTransition is { } recovered)
        {
            if (recovered.ToGame != ActiveGame || !DurableFile.Retire(_session.PendingTransitionPath))
            {
                _process.Terminate();
                _process = null;
                _ipc.Dispose();
                _ipc = null;
                return "could not retire the durable cross-game transition";
            }

            _session.RecoveredTransition = null;
        }

        if (!SessionPaths.RecordSelectedSession(_session, out var selectionError))
        {
            _process.Terminate();
            return selectionError;
        }

        _launchEntrance = bootEntrance;
        _launchOotAge = ootAge;
        _launchedAt = DateTime.UtcNow;
        if (!_relaunching) _launchRetries = 0;
        _relaunching = false;
        _outboxOffset = DurableFile.ReadOffset(ActiveOutboxCursor) ?? 0;
        _session.OpenFileSelect = false;
        _lastNativeSaveRequest = 0;
        _pendingTransition = null;
        _ipcSettingsDirty = true;
        LogEvent($"{GameInfo.Title(ActiveGame)} started.");
        StateChanged?.Invoke();
        return null;
    }

    private string? ReadRecoveryFailure()
    {
        try
        {
            var path = _session.StatePath + ".recovery-result.json";
            if (!File.Exists(path)) return null;
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1
                || root.GetProperty("saveId").GetString() != _session.SaveId
                || root.GetProperty("slot").GetUInt32() != _session.LogicalSlot)
                return "Save recovery returned an invalid result. Existing save files were retained.";
            return root.GetProperty("restored").GetBoolean() ? null
                : "Save recovery stopped: " + root.GetProperty("reason").GetString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return "Could not read the save recovery result: " + ex.Message;
        }
    }

    private string ActiveInbox => ActiveGame == Game.Oot ? _session.OotInbox : _session.MmInbox;

    private string ActiveOutbox => ActiveGame == Game.Oot ? _session.OotOutbox : _session.MmOutbox;

    private string ActiveOutboxCursor =>
        ActiveGame == Game.Oot ? _session.OotOutboxCursor : _session.MmOutboxCursor;

    private void Tick()
    {
        if (_ended || _starting)
        {
            return;
        }

        ExpirePendingSwitch();

        if (_recoveryPending && File.Exists(_session.StatePath + ".recovery-result.json"))
        {
            _recoveryPending = false;
            if (ReadRecoveryFailure() is { } failure)
            {
                _process?.Terminate();
                EndSession(failure);
                return;
            }
            LogEvent("Recovered the MM save from its backup. The damaged original was preserved.");
        }

        DrainActiveOutbox();
        SyncCompletedFromState(force: false);
        _multiworld?.Tick(CompletedChecks, ActiveInbox);
        if (_multiworld is not null)
        {
            _modelSync?.Tick(_multiworld.Ready, _multiworld.ConnectionGeneration,
                             IsRunning ? ActiveGame : null);
        }
        ReconcileInventory();
        LayoutEmbeddedGame();
        PublishIpcSettingsIfDirty();

        if (_stateDirty)
        {
            _stateDirty = false;
            StateChanged?.Invoke();
        }

        if (_pendingTransition is { } transition)
        {
            if (DateTime.UtcNow >= _handoffDeadline)
            {
                _process?.Terminate();
                _modelSync?.InstallNow();
                ActiveGame = transition.ToGame;
                _session.RecoveredTransition = transition;
                LogEvent($"Crossing over to {GameInfo.Title(transition.ToGame)}…");
                StartActiveGame(transition.ToNativeId, transition.OotAge);
            }
        }
        else if (_process is not null && !_process.Running)
        {
            _pendingSwitch = null;
            var crash = DescribeCrash(_process);
            if (ShouldRelaunchAfterStartupCrash(_process.ExitCode, DateTime.UtcNow - _launchedAt, _launchRetries)
                && ReadRecoveryFailure() is null)
            {
                _launchRetries++;
                _relaunching = true;
                LogEvent(crash ?? $"{GameInfo.Title(ActiveGame)} crashed while starting.");
                LogEvent($"Starting {GameInfo.Title(ActiveGame)} again.");
                _process.Dispose();
                _process = null;
                StartActiveGame(_launchEntrance, _launchOotAge);
                return;
            }
            EndSession(ReadRecoveryFailure() ?? crash ?? $"{GameInfo.Title(ActiveGame)} exited — session closed.");
        }
    }

    private string? DescribeCrash(GameProcess process)
    {
        if (process.ExitCode is not { } exitCode || !CrashReports.IsCrashExit(exitCode))
        {
            return null;
        }

        var exe = ActiveGame == Game.Oot ? _session.OotExe : _session.MmExe;
        var report = CrashReports.Find(exe, process.Pid, _launchedAt);
        return CrashReports.Describe(GameInfo.Title(ActiveGame), exe, exitCode,
            report is null ? null : CrashReports.Bundle(report));
    }

    public static readonly TimeSpan StartupCrashWindow = TimeSpan.FromSeconds(30);

    public const int StartupCrashRetries = 2;

    public static bool ShouldRelaunchAfterStartupCrash(int? exitCode, TimeSpan sinceLaunch, int retries) =>
        exitCode is { } code && code != 0 && sinceLaunch <= StartupCrashWindow && retries < StartupCrashRetries;

    private void DrainActiveOutbox()
    {
        var read = FilePacketBridge.ReadPacketLines(ActiveOutbox, _outboxOffset);
        if (!read.Success || read.Packets.Count == 0)
        {
            return;
        }

        var collected = false;
        for (var i = 0; i < read.Packets.Count; i++)
        {
            var packet = read.Packets[i];
            if (AnchorPackets.ParseCheckComplete(packet) is { } check)
            {
                var key = Identity.CheckKey(check.CheckGame, check.CheckId);
                if (CompletedChecks.Add(key))
                {
                    LogCheckCollected(key);
                    collected = true;
                    _stateDirty = true;
                }
            }
            else if (AnchorPackets.ParseCrossGameTransition(packet) is { } transition)
            {
                AcceptTransition(transition, packet);
            }
        }

        if (collected)
        {
            SaveCompletedState();
        }

        _outboxOffset = read.NextOffset;
        DurableFile.WriteOffset(ActiveOutboxCursor, _outboxOffset);
    }

    private void SyncCompletedFromState(bool force)
    {
        try
        {
            if (!File.Exists(_session.StatePath))
            {
                return;
            }

            var stamp = File.GetLastWriteTimeUtc(_session.StatePath);
            if (!force && stamp == _stateStamp)
            {
                return;
            }

            _stateStamp = stamp;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_session.StatePath));
            if (!doc.RootElement.TryGetProperty("completedChecks", out var checks)
                || checks.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return;
            }

            var changed = false;
            foreach (var entry in checks.EnumerateArray())
            {
                if (entry.ValueKind == System.Text.Json.JsonValueKind.String
                    && ResolveLocalCheck(entry.GetString()!) is { } placement
                    && CompletedChecks.Add(Identity.CheckKey(placement.Check.Game, placement.Check.Id)))
                {
                    changed = true;
                    if (_initialSyncDone)
                    {
                        LogCheckCollected(Identity.CheckKey(placement.Check.Game, placement.Check.Id));
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("resolvedCheckRewards", out var resolved)
                && resolved.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var entry in resolved.EnumerateObject())
                {
                    if (ResolveLocalCheck(entry.Name) is not { } placement)
                    {
                        continue;
                    }
                    var key = Identity.CheckKey(placement.Check.Game, placement.Check.Id);
                    if (!CompletedChecks.Contains(key))
                    {
                        continue;
                    }
                    try
                    {
                        var item = System.Text.Json.JsonSerializer.Deserialize<ItemRef>(entry.Value.GetRawText());
                        if (item is not null && item.Id == placement.Item.Id && item.Game == placement.Item.Game
                            && !string.IsNullOrWhiteSpace(item.Name))
                        {
                            _resolvedCheckRewards.TryAdd(key, item);
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("renewalGrants", out var renewals)
                && renewals.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var entry in renewals.EnumerateObject())
                {
                    if (SessionInventory.IsValidItemId(entry.Name)
                        && entry.Value.TryGetInt32(out var granted)
                        && granted > _renewalGrants.GetValueOrDefault(entry.Name))
                    {
                        _renewalGrants[entry.Name] = granted;
                        changed = true;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("endgameBosses", out var bosses)
                && bosses.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in bosses.EnumerateArray())
                {
                    if (entry.ValueKind == System.Text.Json.JsonValueKind.String
                        && Endgame.BossItems.ContainsKey(entry.GetString()!)
                        && _endgameBosses.Add(entry.GetString()!))
                    {
                        changed = true;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("discoveredEntrances", out var entrances)
                && entrances.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in entrances.EnumerateArray())
                {
                    if (entry.ValueKind == System.Text.Json.JsonValueKind.String
                        && _discoveredEntrances.Add(entry.GetString()!)
                        && _entranceMap?.Discover(entry.GetString()!) == true)
                    {
                        EntranceRevision++;
                    }
                }
            }

            if (changed)
            {
                _stateDirty = true;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
        }
        finally
        {
            if (force)
            {
                _initialSyncDone = true;
            }
        }
    }

    private bool SaveCompletedState()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            completedChecks = CompletedChecks.Order(StringComparer.Ordinal),
            resolvedCheckRewards = _resolvedCheckRewards,
            endgameBosses = _endgameBosses.Order(StringComparer.Ordinal),
            renewalGrants = _renewalGrants,
            discoveredEntrances = _discoveredEntrances.Order(StringComparer.Ordinal),
            sessionValues = _session.LogicalSlot is { } slot
                ? new Dictionary<string, string> { ["ootmm.activeSlot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : new Dictionary<string, string>(),
        });
        if (!DurableFile.WriteAtomic(_session.StatePath, json))
        {
            LogEvent("Could not persist check progress; it will be retried.");
            return false;
        }

        try
        {
            _stateStamp = File.GetLastWriteTimeUtc(_session.StatePath);
        }
        catch (IOException)
        {
        }

        return true;
    }

    private void PollGameFocus()
    {
        var isDown = GameWindow.LeftButtonDown();
        var wasDown = _leftButtonWasDown;
        _leftButtonWasDown = isDown;
        if (isDown && !wasDown && GameWindow.IsAlive(_gameChild)
            && GameWindow.OwnerIsForeground(GameHostHandle)
            && GameWindow.CursorOverWindow(GameHostHandle))
        {
            GameWindow.Focus(_gameChild);
        }
    }

    public void SetLauncherKeyboard(bool wanted)
    {
        if (!GameWindow.IsAlive(_gameChild))
        {
            return;
        }

        if (wanted)
        {
            GameWindow.FocusOwner(GameHostHandle);
        }
        else if (GameWindow.OwnerIsActive(GameHostHandle))
        {
            GameWindow.Focus(_gameChild);
        }
    }

    public void OnHostActivated()
    {
        if (GameWindow.IsAlive(_gameChild) && GameWindow.CursorOverWindow(GameHostHandle))
        {
            GameWindow.Focus(_gameChild);
        }
    }

    private void PumpIpc()
    {
        while (_ipc?.TryDequeue(out var ipcEvent) == true && ipcEvent is not null)
        {
            switch (ipcEvent.Type)
            {
                case GameIpcEventType.Connected:
                    _ipc.Send(GameIpcProtocol.BuildWelcome(
                        _session.Seed.SeedId,
                        _session.SaveId,
                        _session.Seed.PlayerId,
                        ActiveGame));
                    _ipc.Send(GameIpcProtocol.BuildInventorySnapshot(
                        _inventory.Revision, _inventory.Items));
                    _ipc.Send(GameIpcProtocol.BuildCompletedChecks(
                        CompletedChecks.Order(StringComparer.Ordinal)));
                    _ipcSettingsDirty = true;
                    LogEvent($"{GameInfo.Title(ActiveGame)} connected to the launcher.");
                    FlushRemoteItems();
                    FlushLiveModels();
                    break;
                case GameIpcEventType.Message:
                    if (GameIpcProtocol.TryParseSaveCompleted(ipcEvent.Message, out var savedRequest, out var saved))
                    {
                        var switching = _pendingSwitch?.RequestId == savedRequest && saved;
                        CompletePendingSwitch(savedRequest, saved);
                        if (switching || _ended)
                            return;
                        break;
                    }
                    if (NativeSaveRequest.TryParse(ipcEvent.Message, out var saveRequest, out _))
                    {
                        HandleNativeSaveRequest(saveRequest!);
                        if (_ended)
                            return;
                        break;
                    }
                    if (GameIpcProtocol.TryParseLocalPlayerPose(ipcEvent.Message, out var pose))
                    {
                        _multiworld?.SubmitLocalPose(pose);
                        break;
                    }
                    if (GameIpcProtocol.TryParsePvpHit(ipcEvent.Message, out var pvpHit))
                    {
                        _multiworld?.SubmitPvpHit(pvpHit);
                        break;
                    }
                    if (GameIpcProtocol.TryParseEndgameReached(ipcEvent.Message, out var boss))
                    {
                        HandleEndgameReached(boss);
                        break;
                    }
                    if (GameIpcProtocol.TryParseDeathLink(ipcEvent.Message, out _))
                    {
                        _multiworld?.SubmitDeathLink();
                        break;
                    }
                    if (GameIpcProtocol.TryParseInventoryDebugSet(
                            ipcEvent.Message, out var itemUpdate, out _)
                        && itemUpdate is not null)
                    {
                        ApplyInventoryDebugSet(itemUpdate);
                        break;
                    }
                    if (GameIpcProtocol.TryParseDebugPresentItem(
                            ipcEvent.Message, out var presentation, out _)
                        && presentation is not null)
                    {
                        SendDebugItemPresentation(presentation);
                        break;
                    }
                    if (GameIpcProtocol.TryParseRenewalGrant(ipcEvent.Message, out var renewalItem))
                    {
                        _renewalGrants[renewalItem] = _renewalGrants.GetValueOrDefault(renewalItem) + 1;
                        if (SaveCompletedState())
                        {
                            ReconcileInventory();
                        }
                        else
                        {
                            _renewalGrants[renewalItem]--;
                        }
                        break;
                    }
                    if (GameIpcProtocol.TryParseEntranceTaken(
                            ipcEvent.Message, out var takenGame, out var takenSource, out var takenSerial))
                    {
                        HandleEntranceTaken(takenGame, takenSource, takenSerial);
                        break;
                    }
                    if (GameIpcProtocol.TryParseLocation(ipcEvent.Message, out var location) && location is not null)
                    {
                        HandleLocation(location);
                        break;
                    }
                    if (GameIpcProtocol.TryParseCheckCollected(
                            ipcEvent.Message, out var checkKey, out var presentationRequested, out _))
                    {
                        HandleCheckCollected(checkKey, presentationRequested);
                        break;
                    }
                    if (GameIpcProtocol.TryParseCrossGameTransition(
                            ipcEvent.Message, out var transition, out _)
                        && transition is not null
                        && AcceptTransition(
                            transition,
                            AnchorPackets.BuildCrossGameTransition(transition, _session.Seed))
                        && !_ipc.Send(GameIpcProtocol.BuildTransitionAccepted(transition)))
                    {
                        CancelPendingTransition("Could not acknowledge the cross-game transition.");
                    }
                    break;
                case GameIpcEventType.Disconnected:
                    if (_process?.Running == true)
                    {
                        LogEvent($"{GameInfo.Title(ActiveGame)} lost its launcher connection.");
                    }
                    break;
                case GameIpcEventType.ProtocolError:
                    LogEvent($"Game IPC error: {ipcEvent.Message}");
                    break;
            }
        }
    }

    private void HandleEntranceTaken(Game game, uint source, ulong serial)
    {
        if (_entranceMap is { } map)
        {
            var key = map.Pack.FindEntrance(game, source)?.Key ?? "";
            map.Leave(key);
            if (map.Discover(key))
            {
                _discoveredEntrances.Add(key);
                if (!SaveCompletedState())
                {
                    _discoveredEntrances.Remove(key);
                    map.Restore(_discoveredEntrances);
                    return;
                }

                EntranceRevision++;
                StateChanged?.Invoke();
            }
        }

        _ipc?.Send(GameIpcProtocol.BuildEntranceAccepted(serial));
    }

    private void HandleLocation(GameLocation location)
    {
        _entranceMap?.Arrive(location.Game, location.Scene, location.Entrance);
        StateChanged?.Invoke();
    }

    private void FlushLiveModels()
    {
        if (_ipc?.IsConnected != true)
        {
            return;
        }

        _pendingLiveModels.RemoveAll(live => live.Game != ActiveGame || !File.Exists(live.Path));
        foreach (var (_, path, player) in _pendingLiveModels)
        {
            _ipc.Send(GameIpcProtocol.BuildMountModelArchive(path, player));
        }

        _pendingLiveModels.Clear();
    }

    private void OnRemoteItemReceived(ushort sourcePlayer, ItemPlacement placement)
    {
        ReconcileInventory();
        var item = placement.Item with
        {
            Name = ProgressiveItemNames.Resolve(
                _session.Seed, placement.Item.Game, placement.Item, _inventory.Items),
        };
        _pendingRemoteItems.Add((sourcePlayer, item));
        FlushRemoteItems();
    }

    private void FlushRemoteItems()
    {
        if (_ipc?.IsConnected != true)
        {
            return;
        }

        foreach (var (sourcePlayer, item) in _pendingRemoteItems)
        {
            _ipc.Send(GameIpcProtocol.BuildPresentItem(
                item,
                item.Game,
                sourcePlayer,
                _session.Seed.PlayerId,
                debug: false,
                viewerPlayer: _session.Seed.PlayerId,
                background: true,
                quiet: !RemoteItemPrompts));
        }

        _pendingRemoteItems.Clear();
    }

    private void SendDebugItemPresentation(DebugPresentItem request)
    {
        if (!AllowDebugMenus)
        {
            LogEvent("Game rejected an item presentation because debug menus are disabled.");
            return;
        }
        if (request.RecipientPlayer > _session.Seed.PlayerCount)
        {
            LogEvent($"Game rejected invalid recipient player {request.RecipientPlayer}.");
            return;
        }

        IReadOnlyDictionary<string, uint> recipientInventory =
            request.RecipientPlayer == _session.Seed.PlayerId
                ? _inventory.Items
                : EmptyItemCounts;
        var item = ProgressiveItemNames.ResolveItem(
            _session.Seed, request.ItemId, recipientInventory);
        if (item is null && DebugTrapNames.TryGetValue(request.ItemId, out var trapName))
        {
            item = new ItemRef(Game.Oot, request.ItemId, trapName);
        }
        if (item is null)
        {
            LogEvent($"Game rejected unknown presentation item {request.ItemId}.");
            return;
        }

        _ipc?.Send(GameIpcProtocol.BuildPresentItem(
            item,
            item.Game,
            _session.Seed.PlayerId,
            request.RecipientPlayer,
            debug: true,
            viewerPlayer: _session.Seed.PlayerId));
    }

    private static readonly IReadOnlyDictionary<string, uint> EmptyItemCounts =
        new Dictionary<string, uint>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> DebugTrapNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OOT_TRAP_ICE"] = "Ice Trap",
            ["OOT_TRAP_FIRE"] = "Fire Trap",
            ["OOT_TRAP_SHOCK"] = "Shock Trap",
            ["OOT_TRAP_DRAIN"] = "Drain Trap",
            ["OOT_TRAP_ANTI_MAGIC"] = "Anti-Magic Trap",
            ["OOT_TRAP_KNOCKBACK"] = "Knockback Trap",
            ["OOT_TRAP_RUPOOR"] = "Rupoor Trap",
        };

    private void ApplyInventoryDebugSet(InventoryDebugSet update)
    {
        if (!AllowDebugMenus)
        {
            LogEvent("Game rejected an inventory edit because debug menus are disabled.");
            return;
        }

        if (!_inventory.TrySetDebugValue(update.ItemId, update.Value))
        {
            LogEvent(_inventory.LastError.Length > 0
                ? _inventory.LastError
                : $"Game rejected unknown item {update.ItemId}.");
            return;
        }

        _ipc?.Send(GameIpcProtocol.BuildInventorySnapshot(
            _inventory.Revision, _inventory.Items));
        StateChanged?.Invoke();
    }

    private void ReconcileInventory()
    {
        if (_inventory.Reconcile(
                CompletedChecks,
                _multiworld?.RemoteItemCounts ?? new Dictionary<string, int>(),
                _endgameBosses,
                _renewalGrants)
            && _ipc is not null)
        {
            _ipc.Send(GameIpcProtocol.BuildInventorySnapshot(
                _inventory.Revision, _inventory.Items));
        }
    }

    private bool IsShuffledCrossGameEntrance(CrossGameTransition transition) =>
        _session.Seed.Entrances.Any(mapping =>
            mapping.IsCrossGame
            && mapping.FromGame == transition.FromGame
            && mapping.ToGame == transition.ToGame
            && mapping.To == transition.ToEntrance
            && mapping.ToNativeId == transition.ToNativeId);

    private bool IsOwlWarp(CrossGameTransition transition) =>
        transition.ToNativeId is { } nativeId && _session.Seed.IsOwlDestination(transition.ToGame, nativeId);

    private bool IsWarpSongTarget(CrossGameTransition transition) =>
        transition.ToNativeId is { } nativeId && _session.Seed.IsSongDestination(transition.ToGame, nativeId);

    private bool IsSpawnTarget(CrossGameTransition transition) =>
        transition.FromGame == Game.Oot
        && transition.ToGame == Game.Mm
        && transition.ToNativeId is { } nativeId
        && _session.Seed.IsSpawnDestination(Game.Mm, nativeId);

    private bool IsBossWarpTarget(CrossGameTransition transition) =>
        transition.ToNativeId is { } nativeId
        && transition.ToEntrance == (transition.ToGame == Game.Oot ? "OoT dungeon exit" : "MM dungeon exit")
        && BossWarp.IsDestinationAllowed(_session.Seed.DungeonWarps, transition.ToGame, nativeId);

    private bool IsFaroresWindTarget(CrossGameTransition transition) =>
        transition.ToNativeId == FaroresWindCrossEntrance
        && transition.ToEntrance == "Farore's Wind"
        && _session.Seed.GetBoolSetting("crossGameFw");

    private static bool IsDungeonRespawnTarget(CrossGameTransition transition) =>
        transition.ToNativeId == DungeonRespawnEntrance && transition.ToEntrance == "Dungeon Entrance";

    private bool AcceptTransition(CrossGameTransition transition, string durablePacket)
    {
        if (_pendingTransition is not null
            || transition.FromGame != ActiveGame
            || transition.ToGame == ActiveGame
            || transition.ToNativeId is null
            || !(IsShuffledCrossGameEntrance(transition) || IsOwlWarp(transition)
                || IsWarpSongTarget(transition) || IsSpawnTarget(transition) || IsBossWarpTarget(transition)
                || IsFaroresWindTarget(transition) || IsDungeonRespawnTarget(transition)))
        {
            return false;
        }
        if (!DurableFile.WriteAtomic(_session.PendingTransitionPath, durablePacket))
        {
            LogEvent("Could not persist the cross-game transition.");
            return false;
        }

        _pendingTransition = transition;
        _handoffDeadline = DateTime.UtcNow.AddMilliseconds(HandoffGraceMs);
        return true;
    }

    private void CancelPendingTransition(string reason)
    {
        _pendingTransition = null;
        DurableFile.Retire(_session.PendingTransitionPath);
        LogEvent(reason);
    }

    private void PublishIpcSettingsIfDirty()
    {
        if (!_ipcSettingsDirty || _ipc?.IsConnected != true)
        {
            return;
        }

        var revision = _settingsRevision + 1;
        if (_ipc.Send(GameIpcProtocol.BuildSettingsSnapshot(revision, BuildRosterMeta())))
        {
            _settingsRevision = revision;
            _ipcSettingsDirty = false;
        }
    }

    private void PumpPresence()
    {
        if (_ended || _starting)
        {
            return;
        }

        PumpIpc();
        if (_ended)
            return;
        _multiworld?.PumpPresence();
        if (_multiworld is not null)
        {
            _modelSync?.PumpSend(_multiworld.Ready);
        }

        PublishRemotePresence();
    }

    private void PublishRemotePresence()
    {
        if (_multiworld is null || _ipc?.IsConnected != true)
        {
            return;
        }

        var (poses, hits) = _multiworld.TakeRemotePresence();
        _ipc.Send(GameIpcProtocol.BuildRemotePlayerPoses(poses));
        foreach (var hit in hits)
        {
            _ipc.Send(GameIpcProtocol.BuildPvpHit(hit));
        }

        foreach (var death in _multiworld.TakeRemoteDeaths())
        {
            _ipc.Send(GameIpcProtocol.BuildDeathLink(death));
        }
    }

    private RosterMeta BuildRosterMeta()
    {
        var transport = _multiworld?.State switch
        {
            null or TransportState.Healthy => "healthy",
            TransportState.Connecting => "connecting",
            TransportState.Offline => "offline",
            TransportState.Syncing => "syncing",
            TransportState.LocalError => "local-error",
            _ => "protocol-error",
        };
        return new RosterMeta(
            ShowNames: true,
            Pvp: _multiworld?.Pvp ?? false,
            DeathLink: (_multiworld?.DeathLink ?? Core.Relay.DeathLinkMode.Off) != Core.Relay.DeathLinkMode.Off,
            SpeedPercent: TimerEnabled ? _speedPercent : 100,
            SpeedSmooth: _speedSmooth,
            TransportState: transport,
            PendingChecks: (uint)(_multiworld?.PendingChecks ?? 0),
            ZTargetHold: _zTargetHold,
            Traps: Traps,
            FairySpotPads: _fairySpotPads,
            PotRolling: _potRolling,
            PvpDamagePercent: _multiworld?.PvpDamagePercent ?? Core.Relay.RelayRules.DefaultPvpDamagePercent,
            SteadyDeityBeams: _steadyDeityBeams);
    }

    private void LogCheckCollected(string checkKey)
    {
        var colon = checkKey.IndexOf(':');
        if (colon <= 0 || GameInfo.TryFromSeedTag(checkKey[..colon]) is not { } game)
        {
            return;
        }

        var checkId = checkKey[(colon + 1)..];
        var placement = _session.Seed.FindPlacement(game, checkId);
        var name = placement?.Check.Name;
        var itemName = _resolvedCheckRewards.TryGetValue(checkKey, out var reward)
            ? reward.Name : placement?.Item.Name ?? "";
        var tag = game == Game.Oot ? "[OoT]" : "[MM]";
        var display = string.IsNullOrEmpty(name) ? checkId : name;
        if (itemName.Length == 0)
        {
            LogEvent($"{tag} {display}");
        }
        else if (!placement!.IsFor(_session.Seed.PlayerId))
        {
            LogEvent($"{tag} {display}: {itemName} (sent to player {placement.OwnerPlayer})");
        }
        else
        {
            LogEvent($"{tag} {display}: found {itemName}");
        }
    }

    private void LayoutEmbeddedGame()
    {
        if (GameHostHandle == 0)
        {
            return;
        }

        var size = GameWindow.ClientSize(GameHostHandle);
        if (!GameWindow.IsAlive(_gameChild))
        {
            _gameChild = GameWindow.FindGameChild(GameHostHandle, _process?.Pid ?? 0);
            if (_gameChild != 0)
            {
                _laidOutSize = size;
                GameWindow.Layout(_gameChild, size.Width, size.Height);
                GameWindow.Focus(_gameChild);
            }

            return;
        }

        if (_laidOutSize != size)
        {
            _laidOutSize = size;
            GameWindow.Layout(_gameChild, size.Width, size.Height);
        }
    }

    private void OnOwnFactConfirmed(Game game, string checkId)
    {
        var key = Identity.CheckKey(game, checkId);
        if (CompletedChecks.Add(key))
        {
            LogCheckCollected(key);
            _stateDirty = true;
            SaveCompletedState();
        }
    }

    private ItemPlacement? ResolveLocalCheck(string checkKey)
    {
        var tag = GameIpcProtocol.SplitCheckKey(checkKey, out var checkId);
        if (GameInfo.TryFromSeedTag(tag) is not { } game)
        {
            return null;
        }
        var placement = _session.Seed.FindPlacement(game, checkId);
        return placement?.SourcePlayer == _session.Seed.PlayerId ? placement : null;
    }

    private void HandleEndgameReached(string boss)
    {
        if (!_endgameBosses.Add(boss))
        {
            return;
        }
        if (!SaveCompletedState())
        {
            _endgameBosses.Remove(boss);
            return;
        }
        LogEvent(boss == Endgame.Ganon ? "Ganon defeated." : "Majora defeated.");
        ReconcileInventory();
        _stateDirty = true;
    }

    private void HandleCheckCollected(string reportedCheckKey, bool presentationRequested = false)
    {
        if (ResolveLocalCheck(reportedCheckKey) is not { } placement)
        {
            LogEvent($"Game reported unknown or foreign-world check {reportedCheckKey}; not recorded.");
            return;
        }

        var game = placement.Check.Game;
        var checkId = placement.Check.Id;
        var checkKey = Identity.CheckKey(game, checkId);
        if (presentationRequested && game != ActiveGame)
        {
            LogEvent("Game requested a reward from a different game instance.");
            return;
        }
        var isNew = !CompletedChecks.Contains(checkKey);
        if (isNew || presentationRequested && !_resolvedCheckRewards.ContainsKey(checkKey))
        {
            ReconcileInventory();
            IReadOnlyDictionary<string, uint> recipientInventory = placement.IsFor(_session.Seed.PlayerId)
                ? _inventory.Items
                : EmptyItemCounts;
            var priorInventory = new Dictionary<string, uint>(recipientInventory, StringComparer.Ordinal);
            if (!isNew && placement.IsFor(_session.Seed.PlayerId)
                && priorInventory.TryGetValue(placement.Item.Id, out var count) && count > 0)
            {
                priorInventory[placement.Item.Id] = count - 1;
            }
            var reward = ProgressiveItemNames.ResolveCheck(_session.Seed, game, checkId, priorInventory);
            if (reward is null)
            {
                return;
            }
            _resolvedCheckRewards[checkKey] = reward.Item;
        }
        if (CompletedChecks.Add(checkKey))
        {
            if (!SaveCompletedState())
            {
                CompletedChecks.Remove(checkKey);
                _resolvedCheckRewards.Remove(checkKey);
                return;
            }

            LogCheckCollected(checkKey);
            _stateDirty = true;
        }

        if (presentationRequested)
        {
            if (!SaveCompletedState())
            {
                return;
            }
            if (_resolvedCheckRewards.TryGetValue(checkKey, out var item))
            {
                _ipc?.Send(GameIpcProtocol.BuildPresentItem(
                    item, item.Game, _session.Seed.PlayerId,
                    placement.IsFor(_session.Seed.PlayerId) ? _session.Seed.PlayerId : placement.OwnerPlayer,
                    debug: false, viewerPlayer: _session.Seed.PlayerId, checkKey: reportedCheckKey));
            }
        }
        _ipc?.Send(GameIpcProtocol.BuildCheckAccepted(reportedCheckKey));
    }

    private void HandleNativeSaveRequest(NativeSaveRequest request)
    {
        void Reject(string reason)
        {
            _ipc?.Send(System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "nativeSaveResult", version = 1, requestId = request.RequestId,
                accepted = false, reason,
            }));
            LogEvent(reason);
        }

        if (SaveSelectionReady is null)
        {
            Reject("This save operation is not available in the current session host.");
            return;
        }
        if (_pendingTransition is not null || _ended
            || !request.MatchesActiveSave(_session.SaveId, _session.LogicalSlot, _lastNativeSaveRequest))
        {
            Reject("The save request does not match the active session or a transition is pending.");
            return;
        }
        DrainActiveOutbox();
        SyncCompletedFromState(force: true);
        if (!SaveCompletedState())
        {
            Reject("Could not persist the current check state before selecting another save.");
            return;
        }
        if (request.Operation == NativeSaveOperation.Copy)
        {
            var select = request with { Operation = NativeSaveOperation.Select };
            if (!SessionPaths.TryPrepareSelectedSession(_session, select with { TargetSlot = request.SourceSlot },
                    _lastNativeSaveRequest, out var source, out var copyError)
                || !SessionPaths.TryPrepareSelectedSession(_session, select, _lastNativeSaveRequest,
                    out var target, out copyError))
            {
                Reject(copyError);
                return;
            }
            _process?.Terminate();
            if (_process?.Running == true)
            {
                Reject("Could not stop the game before copying saves. Please retry.");
                return;
            }
            var copied = NativeSaveCopy.TryExecute(source!, target!, out copyError);
            LogEvent(copied ? $"Copied save {request.SourceSlot + 1} to save {request.TargetSlot + 1} in both games."
                : copyError);
            var root = Path.GetDirectoryName(_session.SessionDir)!;
            var tag = SessionPaths.SeedTag(_session.Seed);
            var key = SessionPaths.SessionBindingKey(_session, tag.Length == 0 ? "default" : tag);
            if (!NativeSaveCopy.RecoverBindings(_session, root, key, out var recoveryError))
            {
                EndSession($"Save copy recovery must finish before restarting: {recoveryError}");
                return;
            }
            if (!SessionPaths.TryPrepareSelectedSession(_session,
                    select with { TargetSlot = request.ActiveSlot }, _lastNativeSaveRequest,
                    out var reopened, out var restartError))
            {
                EndSession(restartError);
                return;
            }
            reopened!.OpenFileSelect = true;
            _ended = true;
            _timer.Stop();
            _focusTimer.Stop();
            _presenceTimer.Stop();
            var copyGame = ActiveGame;
            UiScheduler.Post(() => SaveSelectionReady?.Invoke(reopened, copyGame));
            return;
        }
        if (!SessionPaths.TryPrepareSelectedSession(_session, request, _lastNativeSaveRequest,
                out var selected, out var error)
            || (!NativeSaveCopy.HasNativeSave(selected!, request.TargetSlot)
                && !NativeSaveCopy.TryInherit(_session, selected!, out error))
            || !SharedResourceSaves.TryRead(selected!, out _, out error))
        {
            Reject(error);
            return;
        }
        var inventory = new SessionInventory(selected!.Seed, selected.DebugItemsPath);
        if (!inventory.IsValid)
        {
            Reject(inventory.LastError);
            return;
        }
        _lastNativeSaveRequest = request.RequestId;
        selected.OpenFileSelect = true;
        _ended = true;
        _timer.Stop();
        _focusTimer.Stop();
        _presenceTimer.Stop();
        _process?.Terminate();
        var game = ActiveGame;
        UiScheduler.Post(() => SaveSelectionReady?.Invoke(selected, game));
    }

    private void LogEvent(string message) => EventLogged?.Invoke(message);
}

internal sealed record PendingSwitch(ulong RequestId, DateTime Deadline, uint? OotAge);
