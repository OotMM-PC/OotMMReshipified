using Avalonia.Threading;
using OotMM.Core;

namespace OotMM.Launcher.Session;

/// <summary>
/// Owns one live play session: process lifecycle, outbox pumping, cross-game
/// handoff, and journal-driven tracker state. UI-thread only (DispatcherTimer).
/// </summary>
public sealed class SessionCoordinator : IDisposable
{
    private readonly Core.Session _session;
    private readonly DispatcherTimer _timer;
    private readonly MultiworldSync? _multiworld;
    private GameProcess? _process;
    private nint _gameChild;
    private (int Width, int Height) _laidOutSize;
    private long _outboxOffset;
    private CrossGameTransition? _pendingTransition;
    private DateTime _handoffDeadline;
    private DateTime _stateStamp = DateTime.MinValue;
    private bool _ended;

    public SessionCoordinator(Core.Session session, MultiworldSync? multiworld = null)
    {
        _session = session;
        _multiworld = multiworld;
        ActiveGame = session.RecoveredTransition?.ToGame ?? session.Seed.ResolveStartingGame();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

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
            _multiworld.SyncChanged += () => StateChanged?.Invoke();
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

    public HashSet<string> CompletedChecks { get; } = [];

    public event Action? StateChanged;
    public event Action<string>? EventLogged;
    public event Action<string>? RemoteEventLogged;
    public event Action<string>? Ended;

    public string? Start()
    {
        uint? entrance = null;
        uint? ootAge = null;
        if (_session.RecoveredTransition is { } recovered)
        {
            ActiveGame = recovered.ToGame;
            entrance = recovered.ToNativeId;
            ootAge = recovered.OotAge;
        }

        SyncCompletedFromState(force: true);
        var error = StartActiveGame(entrance, ootAge);
        if (error is null)
        {
            _timer.Start();
        }

        return error;
    }

    public void RestartGame()
    {
        _process?.Terminate();
        LogEvent($"Restarting {GameInfo.Title(ActiveGame)}…");
        StartActiveGame(null, null);
    }

    public void SwitchGame()
    {
        _process?.Terminate();
        ActiveGame = ActiveGame == Game.Oot ? Game.Mm : Game.Oot;
        LogEvent($"Switching to {GameInfo.Title(ActiveGame)}…");
        StartActiveGame(null, null);
    }

    public void EndSession(string reason)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _timer.Stop();
        _process?.Terminate();
        _process?.Dispose();
        _process = null;
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
            for (var slot = 0; slot < 3; slot++)
            {
                File.Delete($"{_session.StatePath}.slot{slot}");
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
        _stateStamp = DateTime.MinValue;
        LogEvent(ok ? "Seed progress reset." : "Seed progress reset finished with errors.");
        StateChanged?.Invoke();
        StartActiveGame(null, null);
        return ok;
    }

    public void Dispose()
    {
        _timer.Stop();
        _process?.Terminate();
        _process?.Dispose();
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
        _gameChild = 0;
        _process = new GameProcess(exe, _session.BootConfigPath, parentWindow: GameHostHandle);
        if (!_process.Valid)
        {
            _process = null;
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
                return "could not retire the durable cross-game transition";
            }

            _session.RecoveredTransition = null;
        }

        _outboxOffset = DurableFile.ReadOffset(ActiveOutboxCursor) ?? 0;
        _pendingTransition = null;
        LogEvent($"{GameInfo.Title(ActiveGame)} started.");
        StateChanged?.Invoke();
        return null;
    }

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
        _multiworld?.Tick(CompletedChecks,
                          ActiveGame == Game.Oot ? _session.OotInbox : _session.MmInbox);
        LayoutEmbeddedGame();

        if (_pendingTransition is { } transition)
        {
            if (DateTime.UtcNow >= _handoffDeadline)
            {
                _process?.Terminate();
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
                if (CompletedChecks.Add(Identity.CheckKey(check.CheckGame, check.CheckId)))
                {
                    var name = _session.Seed.FindPlacement(check.CheckGame, check.CheckId)?.Check.Name;
                    LogEvent($"{(string.IsNullOrEmpty(name) ? check.CheckId : name)}"
                             + (check.ItemName.Length > 0 ? $": found {check.ItemName}" : ""));
                    StateChanged?.Invoke();
                }
            }
            else if (_pendingTransition is null
                     && AnchorPackets.ParseCrossGameTransition(packet) is { } transition
                     && transition.FromGame == ActiveGame && transition.ToGame != ActiveGame)
            {
                if (DurableFile.WriteAtomic(_session.PendingTransitionPath, packet))
                {
                    _pendingTransition = transition;
                    // Give the port a moment to flush its save before the stop.
                    _handoffDeadline = DateTime.UtcNow.AddMilliseconds(1500);
                }
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
                if (entry.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    changed |= CompletedChecks.Add(entry.GetString()!);
                }
            }

            if (changed)
            {
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // Mid-write journal reads retry next tick.
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
        if (!CompletedChecks.Add(Identity.CheckKey(game, checkId)))
        {
            return;
        }

        var placement = _session.Seed.FindPlacement(game, checkId);
        var name = placement?.Check.Name;
        var itemName = placement?.Item.Name ?? "";
        LogEvent($"{(string.IsNullOrEmpty(name) ? checkId : name)}"
                 + (itemName.Length > 0 ? $": found {itemName}" : ""));
        StateChanged?.Invoke();
    }

    private void LogEvent(string message) => EventLogged?.Invoke(message);
}
