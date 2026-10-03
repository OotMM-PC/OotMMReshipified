using System.Text;
using OotMM.Core;
using OotMM.Core.Relay;

namespace OotMM.Launcher.Session;

public enum TransportState
{
    Connecting,
    Offline,
    Syncing,
    Healthy,
    LocalError,
    ProtocolError,
}

/// <summary>Protocol-v4 transport for one session: relay facts ferry into
/// log.v4.jsonl in seq order, the journal diff republishes until acked.</summary>
public sealed class MultiworldSync : IDisposable
{
    private readonly Core.Session _session;
    private readonly RelayClient _relay;
    private readonly string _logPath;
    private readonly string _sidecarPath;
    private readonly string _clientName;

    private readonly HashSet<string> _acked;
    private readonly Dictionary<string, TimeSpan> _publishAttempts = [];
    private readonly Dictionary<ushort, Seed> _otherWorlds = [];
    private readonly Dictionary<ushort, string> _otherWorldHashes = [];
    private readonly HashSet<string> _remoteReceipts = [];
    private readonly Dictionary<string, int> _remoteCounts = [];
    private readonly List<(string Path, string Line)> _pendingWrites = [];
    private ulong _highestFerriedSeq;
    private ulong _relaySeq;
    private ulong _catchUpGeneration;
    private TimeSpan _lastGapCatchUp = TimeSpan.MinValue;
    private TimeSpan _nextAutoHostPrompt;
    private bool _sidecarDirty = true;
    private bool _sidecarWriteFailed;
    private bool _receiptReplayNeeded;
    private bool _writeFailed;
    private string _lastSessionMeta = "";

    private static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    // Live presence: each remote player's newest pose, and PvP hits aimed at us that the running
    // game has not been handed yet.
    private readonly Dictionary<int, (string Line, TimeSpan At)> _remotePoses = new();
    private readonly List<string> _pendingHits = [];
    private readonly List<string> _pendingDeaths = [];
    private readonly HashSet<string> _seenDeaths = [];
    private uint _deathSeq = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly List<string> _deferredInbound = [];

    public MultiworldSync(Core.Session session, string host, ushort port, string clientName)
    {
        _session = session;
        _clientName = clientName;
        _logPath = Path.Combine(session.SessionDir, "log.v4.jsonl");
        _sidecarPath = Path.Combine(session.SessionDir, "foreignplacements.v4.json");

        // An own fact already in the ferry log IS its ack.
        var scan = V4Transport.ScanFerryLog(_logPath, session.Seed.PlayerId);
        _highestFerriedSeq = scan.HighestSeq;
        _relaySeq = scan.HighestSeq;
        _acked = [.. scan.OwnKeys];

        DurableFile.WriteAtomic(Path.Combine(session.SessionDir, "multiworld.v4"), "4\n");

        _relay = new RelayClient(host, port,
            RelayProtocol.BuildHello(session.Seed, session.SaveId, clientName,
                                     (uint)(session.LogicalSlot ?? 0)),
            SeedManifest.Build(session.Seed));
        _relay.StatusLogged += message => EventLogged?.Invoke($"Relay: {message}", true);
    }

    /// <summary>True once the relay's catch-up stream reached its live marker.</summary>
    public bool Live { get; private set; }

    /// <summary>The relay socket can carry room traffic right now.</summary>
    public bool Ready => _relay.Connected && _relay.ProtocolReady;

    /// <summary>Bumped per reconnect; a new generation means queued lines were discarded.</summary>
    public ulong ConnectionGeneration => _relay.ConnectionGeneration;

    /// <summary>Queues a model-sync line for ordered delivery; false when the queue is full,
    /// so chunk streaming pauses instead of dropping mid-archive.</summary>
    public bool SendModelLine(string line) => _relay.QueueLine(line);

    // Server session rules (OOTMM_SESSION_META); players cannot override them.
    public bool Pvp { get; private set; }
    public bool FriendlyFire { get; private set; }
    public bool DeathLink { get; private set; }
    public bool TimerEnabled { get; private set; } = true;
    public int TimerMin { get; private set; }
    public int TimerMax { get; private set; } = 250;

    public event Action? RulesChanged;

    /// <summary>The local player's items placed in other players' worlds, with
    /// collection state from the ferry receipts.</summary>
    public List<(ItemPlacement Placement, bool Collected)> ForeignOwnedChecks()
    {
        var result = new List<(ItemPlacement, bool)>();
        foreach (var (player, world) in _otherWorlds)
        {
            foreach (var placement in world.Placements)
            {
                if (placement.OwnerPlayer == _session.Seed.PlayerId)
                {
                    var key = "v4:" + V4Transport.NaturalKey(player, placement.Check.Game, placement.Check.Id);
                    result.Add((placement, _remoteReceipts.Contains(key)));
                }
            }
        }

        return result;
    }

    public TransportState State { get; private set; } = TransportState.Connecting;
    public int PendingChecks { get; private set; }
    public IReadOnlyDictionary<string, int> RemoteItemCounts => _remoteCounts;
    /// <summary>Bumped whenever a remote receipt lands, so logic/progress recompute.</summary>
    public long ReceiptRevision { get; private set; }
    public string ResumeSummary =>
        $"ferry log resumes at seq {_highestFerriedSeq} ({_acked.Count} own fact(s) pre-acked)";

    /// <summary>(message, isRemote) — remote lines render amber in the log tab.</summary>
    public event Action<string, bool>? EventLogged;
    /// <summary>An own fact confirmed by the relay: mark the check collected locally.</summary>
    public event Action<Game, string>? OwnFactConfirmed;
    /// <summary>A model-sync side-channel line (announce, request, or chunk) arrived.</summary>
    public event Action<string>? ModelLineReceived;
    /// <summary>Remote receipts or transport state changed; refresh logic/progress.</summary>
    public event Action? SyncChanged;
    /// <summary>A loopback relay target is not answering; the owner may host it.</summary>
    public event Action? AutoHostWanted;

    /// <summary>Drains the socket at presence rate so poses arrive at animation rate. Durable
    /// traffic defers to <see cref="Tick"/>, which is far too expensive to run this often.</summary>
    public void PumpPresence()
    {
        var inbound = new List<string>();
        _relay.Pump(inbound);
        var now = Now;
        foreach (var line in inbound)
        {
            switch (RelayProtocol.ExtractStringField(line, "type"))
            {
                case PresencePackets.PlayerState:
                    var source = RelayProtocol.ExtractIntField(line, "sourcePlayer", -1);
                    if (source >= 0 && source != _session.Seed.PlayerId)
                    {
                        _remotePoses[source] = (line, now);
                    }

                    break;
                case PresencePackets.PvpHit:
                    // Delivered to the game exactly once, so only hits aimed at us are kept.
                    if (RelayProtocol.ExtractIntField(line, "targetPlayer") == _session.Seed.PlayerId)
                    {
                        _pendingHits.Add(line);
                    }

                    break;
                case PresencePackets.DeathLink:
                    if (_seenDeaths.Add(RelayProtocol.ExtractStringField(line, "teamId") + ":"
                                        + RelayProtocol.ExtractIntField(line, "sourcePlayer") + ":"
                                        + RelayProtocol.ExtractU64Field(line, "seq")))
                    {
                        _pendingDeaths.Add(line);
                    }

                    break;
                default:
                    _deferredInbound.Add(line);
                    break;
            }
        }
    }

    public void Tick(HashSet<string> completedChecks, string activeInbox)
    {
        PumpPresence();
        var inbound = new List<string>(_deferredInbound);
        _deferredInbound.Clear();
        var now = Now;

        if (!_relay.Connected && _relay.Host is "127.0.0.1" or "localhost" or "::1"
            && now >= _nextAutoHostPrompt)
        {
            _nextAutoHostPrompt = now + TimeSpan.FromSeconds(10);
            AutoHostWanted?.Invoke();
        }

        // One catch-up per connection generation: since=highestFerriedSeq streams
        // exactly the ferry suffix.
        if (_relay.ProtocolReady && _relay.ConnectionGeneration != _catchUpGeneration)
        {
            _catchUpGeneration = _relay.ConnectionGeneration;
            Live = false;
            _relay.QueueLine(FactLog.EncodeCatchUp(_highestFerriedSeq));
        }

        var changed = false;
        var forActiveGame = new List<string>();
        foreach (var line in inbound)
        {
            if (FactLog.DecodeFact(line) is { Seq: not 0 } fact)
            {
                _relaySeq = Math.Max(_relaySeq, fact.Seq);
                var key = V4Transport.NaturalKey(fact.SourcePlayer, fact.Game, fact.CheckId);
                var own = fact.SourcePlayer == _session.Seed.PlayerId;
                switch (V4Transport.ClassifyFerryLine(fact.Seq, _highestFerriedSeq))
                {
                    case V4FerryAction.Append:
                        _highestFerriedSeq = fact.Seq;
                        _pendingWrites.Add((_logPath, line));
                        if (own)
                        {
                            _acked.Add(key);
                            OwnFactConfirmed?.Invoke(fact.Game, fact.CheckId);
                            changed = true;
                        }
                        else
                        {
                            changed |= AddRemoteReceipt(fact.SourcePlayer, fact.Game, fact.CheckId);
                        }

                        break;
                    case V4FerryAction.Duplicate:
                        if (own)
                        {
                            _acked.Add(key);
                        }

                        break;
                    case V4FerryAction.Gap:
                        // Never append out of order; re-sync from the ferry cursor (rate limited).
                        if (_lastGapCatchUp == TimeSpan.MinValue
                            || now - _lastGapCatchUp >= TimeSpan.FromSeconds(2))
                        {
                            _lastGapCatchUp = now;
                            EventLogged?.Invoke(
                                $"Relay: ferry gap (seq {fact.Seq} after {_highestFerriedSeq}); requesting catch-up",
                                true);
                            _relay.QueueLine(FactLog.EncodeCatchUp(_highestFerriedSeq));
                        }

                        break;
                }

                continue;
            }

            if (FactLog.DecodeAck(line) is { } ack)
            {
                _acked.Add(V4Transport.NaturalKey(ack.SourcePlayer, ack.Game, ack.CheckId));
                _relaySeq = Math.Max(_relaySeq, ack.Seq);
                continue;
            }

            if (FactLog.DecodeLive(line))
            {
                Live = true;
                continue;
            }

            var type = RelayProtocol.ExtractStringField(line, "type");
            if (type == RelayProtocol.SeedManifest)
            {
                if (SeedManifest.Parse(line) is { } manifest
                    && manifest.Seed.PlayerId != _session.Seed.PlayerId)
                {
                    var known = _otherWorldHashes.GetValueOrDefault(manifest.Seed.PlayerId, "");
                    if (known != manifest.ManifestHash)
                    {
                        _otherWorldHashes[manifest.Seed.PlayerId] = manifest.ManifestHash;
                        _otherWorlds[manifest.Seed.PlayerId] = manifest.Seed;
                        _sidecarDirty = true;
                        _receiptReplayNeeded = true;
                        EventLogged?.Invoke(
                            $"Relay: received player {manifest.Seed.PlayerId}'s world manifest.", true);
                    }
                }

                continue;
            }

            if (type == RelayProtocol.Status)
            {
                if (RelayProtocol.ExtractStringField(line, "level") == "error")
                {
                    EventLogged?.Invoke(
                        $"Relay error: {RelayProtocol.ExtractStringField(line, "message")}", true);
                }

                continue;
            }

            if (type == RelayProtocol.SessionMeta)
            {
                if (line != _lastSessionMeta)
                {
                    _lastSessionMeta = line;
                    Pvp = line.Contains("\"pvp\":true");
                    FriendlyFire = line.Contains("\"friendlyFire\":true");
                    DeathLink = line.Contains("\"deathLink\":true");
                    TimerEnabled = !line.Contains("\"timerEnabled\":false");
                    TimerMin = RelayProtocol.ExtractIntField(line, "timerMin");
                    TimerMax = RelayProtocol.ExtractIntField(line, "timerMax", 250);
                    var rules = new List<string>();
                    if (Pvp)
                    {
                        rules.Add(FriendlyFire ? "PvP with friendly fire" : "PvP");
                    }

                    if (DeathLink)
                    {
                        rules.Add("global death link");
                    }

                    EventLogged?.Invoke(
                        rules.Count > 0
                            ? $"Session rules: {string.Join(", ", rules)} enabled."
                            : "Session rules received.", true);
                    RulesChanged?.Invoke();
                }

                continue;
            }

            if (type is PresencePackets.ModAnnounce or PresencePackets.ModRequest
                or PresencePackets.ModChunk)
            {
                ModelLineReceived?.Invoke(line);
                continue;
            }

            if (type == RelayProtocol.WorldFlag)
            {
                // og coop shared-world flags: the game dedups and applies the bit.
                forActiveGame.Add(line);
                continue;
            }

            HandleServerConsoleLine(type, line, forActiveGame);
        }

        if (_receiptReplayNeeded)
        {
            _receiptReplayNeeded = false;
            changed |= ReplayFerryReceipts();
        }

        if (_sidecarDirty)
        {
            if (DurableFile.WriteAtomic(_sidecarPath,
                    V4Transport.BuildForeignPlacementsSidecar(_session.Seed, _otherWorlds)))
            {
                _sidecarDirty = false;
                if (_sidecarWriteFailed)
                {
                    EventLogged?.Invoke("Foreign-placements sidecar publication recovered.", true);
                }

                _sidecarWriteFailed = false;
            }
            else if (!_sidecarWriteFailed)
            {
                _sidecarWriteFailed = true;
                EventLogged?.Invoke("Could not publish the foreign-placements sidecar; retrying.", true);
            }
        }

        foreach (var order in forActiveGame)
        {
            _pendingWrites.Add((activeInbox, order));
        }

        FlushPendingWrites();

        if (_relay.Connected && _relay.ProtocolReady)
        {
            var journal = _session.Seed.Placements
                .Select(p => new V4JournalCheck(p.Check.Game, p.Check.Id,
                    completedChecks.Contains(Identity.CheckKey(p.Check.Game, p.Check.Id))))
                .ToList();
            foreach (var fact in V4Transport.SelectFactsToPublish(
                         journal, _session.Seed.PlayerId, _acked, _publishAttempts, now,
                         TimeSpan.FromSeconds(2), maxBatch: 32))
            {
                _relay.QueueLine(FactLog.EncodeFact(fact));
            }
        }

        changed |= UpdateState(completedChecks);
        if (changed)
        {
            SyncChanged?.Invoke();
        }
    }

    /// <summary>Publishes the running game's pose to the room. Lossy by design: the ephemeral
    /// queue keeps one slot, so a backed-up connection sends the newest pose, never a backlog.
    /// </summary>
    public void SubmitLocalPose(string poseBody)
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PlayerState, poseBody, _session.Seed, _clientName);
        if (line.Length > 0)
        {
            _relay.QueueEphemeralLine("pose", line);
        }
    }

    /// <summary>Relays an attack the game's puppet hurtboxes registered. Hits are events, so
    /// they go through the reliable queue rather than the last-value slot.</summary>
    public void SubmitPvpHit(string hitBody)
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.PvpHit, hitBody, _session.Seed, "");
        if (line.Length > 0)
        {
            _relay.QueueLine(line);
        }
    }

    public void SubmitDeathLink()
    {
        var line = GameIpcProtocol.BuildRelayPresenceLine(
            PresencePackets.DeathLink, $"{{\"seq\":{++_deathSeq}}}", _session.Seed, "");
        if (line.Length > 0)
        {
            _relay.QueueLine(line);
        }
    }

    public List<string> TakeRemoteDeaths()
    {
        var deaths = new List<string>(_pendingDeaths);
        _pendingDeaths.Clear();
        return deaths;
    }

    /// <summary>The roster for the running game: every live remote pose, plus the hits aimed at
    /// us since the last call. Hits are handed over once and then dropped.</summary>
    public (List<string> Poses, List<string> Hits) TakeRemotePresence()
    {
        var cutoff = Now - TimeSpan.FromSeconds(3);
        foreach (var stale in _remotePoses
                     .Where(entry => entry.Value.At < cutoff)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            _remotePoses.Remove(stale);
        }

        var hits = new List<string>(_pendingHits);
        _pendingHits.Clear();
        return (_remotePoses.Values.Select(entry => entry.Line).ToList(), hits);
    }

    public void Dispose() => _relay.Dispose();

    private bool UpdateState(HashSet<string> completedChecks)
    {
        var pending = _session.Seed.Placements.Count(p =>
            completedChecks.Contains(Identity.CheckKey(p.Check.Game, p.Check.Id))
            && !_acked.Contains(V4Transport.NaturalKey(_session.Seed.PlayerId, p.Check.Game, p.Check.Id)));

        TransportState state;
        if (_writeFailed)
        {
            state = TransportState.LocalError;
        }
        else if (_relay.ProtocolError.Length > 0)
        {
            state = TransportState.ProtocolError;
        }
        else if (!_relay.Connected || !_relay.ProtocolReady)
        {
            state = pending == 0 ? TransportState.Connecting : TransportState.Offline;
        }
        else if (pending != 0 || _relaySeq > _highestFerriedSeq)
        {
            state = TransportState.Syncing;
        }
        else
        {
            state = TransportState.Healthy;
        }

        var changed = state != State || pending != PendingChecks;
        State = state;
        PendingChecks = pending;
        return changed;
    }

    private void HandleServerConsoleLine(string type, string line, List<string> forActiveGame)
    {
        if (type == RelayProtocol.Chat)
        {
            EventLogged?.Invoke(
                $"{RelayProtocol.ExtractStringField(line, "from")}: "
                + RelayProtocol.ExtractStringField(line, "text"), true);
            return;
        }

        if (type is not (RelayProtocol.Teleport or RelayProtocol.AdminUnlockAll
            or RelayProtocol.AdminUnlockCheck))
        {
            return;
        }

        // Only orders addressed to our player are forwarded to the game.
        var who = RelayProtocol.ExtractStringField(line, "who");
        var forMe = who == "all" && type != RelayProtocol.Teleport;
        if (!forMe && who.StartsWith("player-id=", StringComparison.Ordinal))
        {
            forMe = int.TryParse(who.AsSpan(10), out var id) && id == _session.Seed.PlayerId;
        }
        else if (!forMe)
        {
            forMe = who.Length > 0 && who == _clientName;
        }

        if (!forMe)
        {
            return;
        }

        if (type == RelayProtocol.Teleport)
        {
            // Username destinations need the presence roster (not yet ported).
            var dest = RelayProtocol.ExtractStringField(line, "dest");
            if (!dest.StartsWith("entrance=", StringComparison.Ordinal)
                && !dest.StartsWith("player-id=", StringComparison.Ordinal))
            {
                EventLogged?.Invoke($"Teleport order dropped: unknown destination '{dest}'.", true);
                return;
            }

            EventLogged?.Invoke($"Teleport order: {dest}", true);
            forActiveGame.Add($"{{\"type\":\"{RelayProtocol.Teleport}\",\"dest\":\"{dest}\"}}");
            return;
        }

        if (type == RelayProtocol.AdminUnlockAll)
        {
            var game = RelayProtocol.ExtractStringField(line, "game");
            if (game != "oot" && game != "mm")
            {
                game = "all";
            }

            EventLogged?.Invoke($"Unlock-all order: game={game}", true);
            forActiveGame.Add($"{{\"type\":\"{RelayProtocol.AdminUnlockAll}\",\"game\":\"{game}\"}}");
            return;
        }

        var checkId = RelayProtocol.ExtractStringField(line, "checkId");
        var commandId = RelayProtocol.ExtractStringField(line, "commandId");
        if (checkId.Length > 0 && commandId.Length > 0)
        {
            EventLogged?.Invoke($"Unlock-check order: {checkId}", true);
            forActiveGame.Add($"{{\"type\":\"{RelayProtocol.AdminUnlockCheck}\",\"checkId\":\"{checkId}\""
                              + $",\"commandId\":\"{commandId}\",\"grant\":false}}");
        }
    }

    private bool AddRemoteReceipt(ushort sourcePlayer, Game game, string checkId, bool quiet = false)
    {
        // A foreign fact counts only when its placement's reward is addressed to us.
        if (_otherWorlds.GetValueOrDefault(sourcePlayer) is not { } world
            || world.FindPlacement(game, checkId) is not { } placement
            || placement.OwnerPlayer != _session.Seed.PlayerId)
        {
            return false;
        }

        var key = "v4:" + V4Transport.NaturalKey(sourcePlayer, game, checkId);
        if (!_remoteReceipts.Add(key))
        {
            return false;
        }

        _remoteCounts[placement.Item.Id] = _remoteCounts.GetValueOrDefault(placement.Item.Id) + 1;
        ReceiptRevision++;
        if (!quiet)
        {
            var tag = game == Game.Oot ? "[OoT]" : "[MM]";
            EventLogged?.Invoke(
                $"{tag} Received {placement.Item.Name} from player {sourcePlayer}'s world.", true);
        }

        return true;
    }

    private bool ReplayFerryReceipts()
    {
        var changed = false;
        var read = FilePacketBridge.ReadPacketLines(_logPath, 0);
        foreach (var line in read.Packets)
        {
            if (FactLog.DecodeFact(line) is { Seq: not 0 } fact
                && fact.SourcePlayer != _session.Seed.PlayerId)
            {
                // Historical receipts count for logic but must not flood the log.
                changed |= AddRemoteReceipt(fact.SourcePlayer, fact.Game, fact.CheckId, quiet: true);
            }
        }

        return changed;
    }

    private void FlushPendingWrites()
    {
        while (_pendingWrites.Count > 0)
        {
            var path = _pendingWrites[0].Path;
            var batch = new List<string>();
            var taken = 0;
            foreach (var (p, line) in _pendingWrites)
            {
                if (p != path)
                {
                    break;
                }

                batch.Add(line);
                taken++;
            }

            if (FilePacketBridge.AppendPacketLines(path, batch) != batch.Count)
            {
                if (!_writeFailed)
                {
                    _writeFailed = true;
                    EventLogged?.Invoke("Durable session write failed; retrying.", true);
                }

                return;
            }

            _pendingWrites.RemoveRange(0, taken);
        }

        if (_writeFailed)
        {
            _writeFailed = false;
            EventLogged?.Invoke("Durable session writes recovered.", true);
        }
    }
}
