using Avalonia.Threading;
using OotMM.Core;

namespace OotMM.Launcher.Session;

/// <summary>
/// Owns one live play session: process lifecycle, outbox pumping, cross-game
/// handoff, and journal-driven tracker state. UI-thread only (DispatcherTimer).
/// </summary>
public sealed class SessionCoordinator : IDisposable
{
    private const int HandoffGraceMs = 400;

    private readonly Core.Session _session;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _focusTimer;
    private readonly DispatcherTimer _presenceTimer;
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
    private DateTime _stateStamp = DateTime.MinValue;
    private bool _stateDirty;
    private bool _initialSyncDone;
    private bool _ended;
    private int _speedPercent = 100;
    private bool _speedSmooth;
    private bool _ipcSettingsDirty = true;
    private long _settingsRevision;
    private long _forceUnlockSeq;

    public SessionCoordinator(Core.Session session, MultiworldSync? multiworld = null,
                              ModelSyncCoordinator? modelSync = null)
    {
        _session = session;
        _multiworld = multiworld;
        _modelSync = modelSync;
        _inventory = new SessionInventory(session.Seed, session.DebugItemsPath);
        ActiveGame = session.RecoveredTransition?.ToGame ?? session.Seed.ResolveStartingGame();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        // Clicks over the embedded game never reach the UI framework, so a fast
        // poll hands keyboard focus to the game when one lands there.
        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _focusTimer.Tick += (_, _) => PollGameFocus();
        // Puppets replay joint tables verbatim, so poses must arrive at the game's logic rate;
        // at the session tick's rate the animation visibly steps.
        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _presenceTimer.Tick += (_, _) => PumpPresence();

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
            _multiworld.SyncChanged += () =>
            {
                _stateDirty = true;
                _ipcSettingsDirty = true;
            };
            _multiworld.RulesChanged += () =>
            {
                SpeedPercent = _speedPercent; // re-clamp into the server's allowed range
                _ipcSettingsDirty = true;
                StateChanged?.Invoke();
            };
            if (_modelSync is not null)
            {
                _multiworld.ModelLineReceived += _modelSync.HandleLine;
                _modelSync.Logged += LogEvent;
            }
        }
    }

    public Core.Session Session => _session;

    public MultiworldSync? Multiworld => _multiworld;

    public Game ActiveGame { get; private set; }

    public bool IsRunning => _process?.Running == true;

    /// <summary>Native host the game window embeds into; games launched while it
    /// is 0 open their own top-level window instead.</summary>
    public nint GameHostHandle { get; set; }

    public void OpenInGameMenu() => GameWindow.PostEscape(_gameChild);

    public bool TimerEnabled => _multiworld?.TimerEnabled ?? true;

    public int TimerMin => Math.Max(0, Math.Min(_multiworld?.TimerMin ?? 0, TimerMax));

    public int TimerMax => Math.Max(0, _multiworld?.TimerMax ?? 250);

    /// <summary>Game speed in percent (100 = vanilla), sent live over IPC and
    /// clamped to the server's allowed range.</summary>
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

    public bool AllowDebugMenus
    {
        get => _session.AllowDebugMenus;
        set => _session.AllowDebugMenus = value; // applies on the next game boot
    }

    /// <summary>Force-completes an open check through the running game's admin
    /// inbox; the game grants the item exactly like a real collection.</summary>
    public bool ForceUnlock(string checkId)
    {
        if (_process is not { Running: true } || _pendingTransition is not null)
        {
            return false;
        }

        var commandId = "launcher-force-"
                        + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000
                        + "-" + ++_forceUnlockSeq;
        var packet = $"{{\"type\":\"OOTMM_ADMIN_UNLOCK_CHECK\",\"checkId\":\"{checkId}\""
                     + $",\"commandId\":\"{commandId}\",\"grant\":true}}";
        var adminInbox = ActiveInbox + $".slot{_session.LogicalSlot ?? 0}.admin.jsonl";
        if (FilePacketBridge.AppendPacketLines(adminInbox, [packet]) != 1)
        {
            LogEvent($"Force-unlock failed to queue for {checkId}.");
            return false;
        }

        return true;
    }

    public HashSet<string> CompletedChecks { get; } = [];

    public event Action? StateChanged;
    public event Action<string>? EventLogged;
    public event Action<string>? RemoteEventLogged;
    public event Action<string>? Ended;

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
        if (error is null)
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
        // The dead game's archives just unlocked: model sets learned mid-session mount now.
        _modelSync?.InstallNow();
        LogEvent($"Restarting {GameInfo.Title(ActiveGame)}…");
        StartActiveGame(null, null);
    }

    /// <summary>Dev-mode game switch. <paramref name="ootAge"/> (0 adult, 1 child) rides the
    /// boot config so MM's crossAge mapper arrives in the chosen age; OoT ignores it.</summary>
    public void SwitchGame(uint? ootAge = null)
    {
        _process?.Terminate();
        _modelSync?.InstallNow();
        ActiveGame = ActiveGame == Game.Oot ? Game.Mm : Game.Oot;
        LogEvent($"Switching to {GameInfo.Title(ActiveGame)}…");
        StartActiveGame(null, ActiveGame == Game.Mm ? ootAge : null);
    }

    public void EndSession(string reason)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _timer.Stop();
        _focusTimer.Stop();
        _presenceTimer.Stop();
        _process?.Terminate();
        _process?.Dispose();
        _process = null;
        _ipc?.Dispose();
        _ipc = null;
        // The dead game's mods just unlocked; any install the session blocked lands now.
        _modelSync?.InstallNow();
        _multiworld?.Dispose();
        Ended?.Invoke(reason);
    }

    /// <summary>Solo only, mirroring the C++ refusal for multiworld seeds.</summary>
    public bool ResetSeedProgress()
    {
        if (_session.Seed.IsMultiworld)
        {
            LogEvent("Reset refused: multiworld progress requires an offline epoch reset.");
            return false;
        }

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

            // A surviving applied/triforce ledger would suppress every re-grant onto the recreated saves.
            var stateName = Path.GetFileName(_session.StatePath);
            foreach (var sidecar in Directory.EnumerateFiles(_session.SessionDir))
            {
                var name = Path.GetFileName(sidecar);
                if (name.StartsWith(stateName + ".applied", StringComparison.Ordinal)
                    || name.StartsWith(stateName + ".triforce", StringComparison.Ordinal))
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
        _timer.Stop();
        _focusTimer.Stop();
        _presenceTimer.Stop();
        _process?.Terminate();
        _process?.Dispose();
        _ipc?.Dispose();
        _modelSync?.InstallNow();
        _multiworld?.Dispose();
    }

    private string? StartActiveGame(uint? bootEntrance, uint? ootAge)
    {
        var boot = SessionPaths.MakeBootConfig(_session, ActiveGame, bootEntrance, ootAge);
        if (!DurableFile.WriteAtomic(_session.BootConfigPath, boot.Serialize()))
        {
            return $"cannot durably publish boot configuration {_session.BootConfigPath}";
        }

        if (!_session.Seed.IsMultiworld)
        {
            File.WriteAllText(ActiveOutbox, "");
            DurableFile.WriteOffset(ActiveOutboxCursor, 0);
        }

        var exe = ActiveGame == Game.Oot ? _session.OotExe : _session.MmExe;
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
            // Retire the durable transition only once its destination is live.
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

        _outboxOffset = DurableFile.ReadOffset(ActiveOutboxCursor) ?? 0;
        _pendingTransition = null;
        _ipcSettingsDirty = true;
        LogEvent($"{GameInfo.Title(ActiveGame)} started.");
        StateChanged?.Invoke();
        return null;
    }

    private string ActiveInbox => ActiveGame == Game.Oot ? _session.OotInbox : _session.MmInbox;

    private string ActiveOutbox => ActiveGame == Game.Oot ? _session.OotOutbox : _session.MmOutbox;

    private string ActiveOutboxCursor =>
        ActiveGame == Game.Oot ? _session.OotOutboxCursor : _session.MmOutboxCursor;

    private void Tick()
    {
        if (_ended)
        {
            return;
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

        // Check bursts (drain, catch-up replay) coalesce into one refresh per tick;
        // per-packet notifications made the sidebar recompute logic N times over.
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
            EndSession($"{GameInfo.Title(ActiveGame)} exited — session closed.");
        }
    }

    private void DrainActiveOutbox()
    {
        var read = FilePacketBridge.ReadPacketLines(ActiveOutbox, _outboxOffset);
        if (!read.Success || read.Packets.Count == 0)
        {
            return;
        }

        for (var i = 0; i < read.Packets.Count; i++)
        {
            var packet = read.Packets[i];
            if (AnchorPackets.ParseCheckComplete(packet) is { } check)
            {
                var key = Identity.CheckKey(check.CheckGame, check.CheckId);
                if (CompletedChecks.Add(key))
                {
                    LogCheckCollected(key);
                    _stateDirty = true;
                }
            }
            else if (AnchorPackets.ParseCrossGameTransition(packet) is { } transition)
            {
                AcceptTransition(transition, packet);
            }
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
                    && CompletedChecks.Add(entry.GetString()!))
                {
                    changed = true;
                    // The first sync reconciles resumed progress and must not
                    // flood the log; later syncs log fresh collections.
                    if (_initialSyncDone)
                    {
                        LogCheckCollected(entry.GetString()!);
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
            // Mid-write journal reads retry next tick.
        }
        finally
        {
            if (force)
            {
                _initialSyncDone = true;
            }
        }
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

    /// <summary>Focus arriving at the launcher (alt-tab back) belongs to the game
    /// when the cursor is over the game area.</summary>
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
                    _ipcSettingsDirty = true;
                    LogEvent($"{GameInfo.Title(ActiveGame)} connected to the launcher.");
                    break;
                case GameIpcEventType.Message:
                    // Presence is the hottest message on the pipe, so it is matched first.
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

        var item = _session.Seed.Placements
            .Select(placement => placement.Item)
            .Concat(_session.Seed.StartingItems.Select(starting => starting.Item))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, request.ItemId, StringComparison.Ordinal));
        if (item is null && DebugTrapNames.TryGetValue(request.ItemId, out var trapName))
        {
            item = new ItemRef(Game.Oot, request.ItemId, trapName);
        }
        if (item is null)
        {
            LogEvent($"Game rejected unknown presentation item {request.ItemId}.");
            return;
        }

        IReadOnlyDictionary<string, uint> recipientInventory =
            request.RecipientPlayer == _session.Seed.PlayerId
                ? _inventory.Items
                : EmptyItemCounts;
        var itemName = ProgressiveItemNames.Resolve(
            _session.Seed, item.Game, item, recipientInventory);
        _ipc?.Send(GameIpcProtocol.BuildPresentItem(
            item with { Name = itemName },
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
                _multiworld?.RemoteItemCounts ?? new Dictionary<string, int>())
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
        transition.ToNativeId is { } nativeId && _session.Seed.EntrancesOwl.Contains(nativeId);

    private bool IsWarpSongTarget(CrossGameTransition transition) =>
        transition.ToNativeId is { } nativeId && _session.Seed.EntrancesSong.Contains(nativeId);

    private bool AcceptTransition(CrossGameTransition transition, string durablePacket)
    {
        if (_pendingTransition is not null
            || transition.FromGame != ActiveGame
            || transition.ToGame == ActiveGame
            || transition.ToNativeId is null
            || !(IsShuffledCrossGameEntrance(transition) || IsOwlWarp(transition) || IsWarpSongTarget(transition)))
        {
            return false;
        }
        if (!DurableFile.WriteAtomic(_session.PendingTransitionPath, durablePacket))
        {
            LogEvent("Could not persist the cross-game transition.");
            return false;
        }

        _pendingTransition = transition;
        // The game saves only once it sees the acknowledgement, so it needs the grace before termination.
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

    /// <summary>Carries presence in both directions at the game's logic rate: the game's own
    /// pose out to the room, and the room's newest poses back down.</summary>
    private void PumpPresence()
    {
        if (_ended)
        {
            return;
        }

        PumpIpc();
        _multiworld?.PumpPresence();
        if (_multiworld is not null)
        {
            _modelSync?.PumpSend(_multiworld.Ready);
        }

        PublishRemotePresence();
    }

    /// <summary>Pushes the live roster to the running game. Sent every tick even when empty:
    /// the game treats an arriving roster as the signal that this is a multiplayer session, and
    /// its absence as the signal to stop publishing poses.</summary>
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
            DeathLink: _multiworld?.DeathLink ?? false,
            SpeedPercent: TimerEnabled ? _speedPercent : 100,
            SpeedSmooth: _speedSmooth,
            TransportState: transport,
            PendingChecks: (uint)(_multiworld?.PendingChecks ?? 0));
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
        var itemName = placement?.Item.Name ?? "";
        var tag = game == Game.Oot ? "[OoT]" : "[MM]";
        var display = string.IsNullOrEmpty(name) ? checkId : name;
        if (itemName.Length == 0)
        {
            LogEvent($"{tag} {display}");
        }
        else if (placement!.OwnerPlayer != _session.Seed.PlayerId)
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
            // (Re)discover after a launch; size and focus it once found.
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
        }
    }

    private void LogEvent(string message) => EventLogged?.Invoke(message);
}
