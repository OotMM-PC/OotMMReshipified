using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace OotMM.Core.Relay;

public sealed class StoreDirectoryLease : IDisposable
{
    private readonly FileStream? _handle;

    public StoreDirectoryLease(string storeDir)
    {
        try
        {
            Directory.CreateDirectory(storeDir);
            _handle = new FileStream(Path.Combine(storeDir, ".ootmm-world-state.lock"),
                                     FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _handle = null;
        }
    }

    public bool Acquired => _handle is not null;

    public void Dispose() => _handle?.Dispose();
}

public sealed record RoomProvisionReport(bool Success, string Room, string Message);

public sealed class RelayEngine
{
    private readonly string _storeDir;
    private readonly RelayRules _rules;
    private readonly Queue<string> _commands = [];
    private readonly Lock _commandLock = new();
    private readonly List<ClientConn> _clients = [];
    private readonly Dictionary<string, V4RoomStore> _rooms = [];
    private readonly Dictionary<string, string> _progressSeeds = [];
    private readonly Dictionary<string, Dictionary<int, SeedManifestRegistration>> _manifests = [];
    private readonly Dictionary<string, Dictionary<int, (string Name, List<string> CheckIds)>> _catalogs = [];

    private const int MaxClientOutboundBytes = 64 * 1024 * 1024;
    private const int MaxClientReceiveFrameBytes = 4 * 1024 * 1024;

    public RelayEngine(string storeDir, RelayRules rules)
    {
        _storeDir = storeDir;
        _rules = rules;
    }

    public Action<string> Log { get; set; } = Console.WriteLine;

    public int BoundPort { get; private set; }

    public void SubmitCommand(string line)
    {
        lock (_commandLock)
        {
            _commands.Enqueue(line);
        }
    }

    public int Run(int port, CancellationToken stop)
    {
        if (_storeDir.Length == 0)
        {
            Log("[relay] refusing to start without a durable world_state directory");
            return 1;
        }

        using var lease = new StoreDirectoryLease(_storeDir);
        if (!lease.Acquired)
        {
            Log("[relay] world_state is already in use by another relay or maintenance operation");
            return 1;
        }

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen(8);
        }
        catch (SocketException)
        {
            Log($"[relay] cannot bind/listen on port {port}");
            return 1;
        }

        BoundPort = ((IPEndPoint)listener.LocalEndPoint!).Port;
        Log($"[relay] multiworld relay listening on port {BoundPort}"
            + (_rules.Pvp ? " (PvP enabled)" : "") + (_rules.Pvp && _rules.FriendlyFire ? " (friendly fire enabled)" : "")
            + (_rules.Pvp && PvpDamage.Clamp(_rules.PvpDamagePercent) != RelayRules.DefaultPvpDamagePercent
                ? $" (PvP damage {PvpDamage.Clamp(_rules.PvpDamagePercent)}%)" : "")
            + (_rules.DeathLink != DeathLinkMode.Off ? $" ({DeathLinkModes.Tag(_rules.DeathLink)} death link enabled)" : "")
            + (_rules.Traps.IsOriginal ? "" : " (custom traps)"));
        Log($"[relay] check state persists in {_storeDir}");

        var lastMeta = SessionMetaLine();
        var nextMetaBroadcast = Environment.TickCount64 + 5000;

        while (!stop.IsCancellationRequested)
        {
            var readList = new List<Socket> { listener };
            var writeList = new List<Socket>();
            foreach (var client in _clients)
            {
                readList.Add(client.Sock);
                if (client.OutboundBytes > 0)
                {
                    writeList.Add(client.Sock);
                }
            }

            try
            {
                Socket.Select(readList, writeList, null, 1_000_000);
            }
            catch (SocketException)
            {
                Log("[relay] select failed");
                break;
            }

            while (true)
            {
                string? command;
                lock (_commandLock)
                {
                    if (!_commands.TryDequeue(out command))
                    {
                        break;
                    }
                }

                RunCommand(command);
            }

            var currentMeta = SessionMetaLine();
            if (currentMeta != lastMeta || Environment.TickCount64 >= nextMetaBroadcast)
            {
                if (currentMeta != lastMeta)
                {
                    lastMeta = currentMeta;
                    Log($"[relay] session rules changed: {currentMeta.TrimEnd()}");
                }

                nextMetaBroadcast = Environment.TickCount64 + 5000;
                foreach (var client in _clients.Where(c => c.Room.Length > 0))
                {
                    client.QueueWire(currentMeta);
                }
            }

            if (readList.Contains(listener))
            {
                try
                {
                    var accepted = listener.Accept();
                    accepted.Blocking = false;
                    _clients.Add(new ClientConn(accepted));
                    Log($"[relay] client connected ({_clients.Count} total)");
                }
                catch (SocketException)
                {
                }
            }

            for (var i = 0; i < _clients.Count;)
            {
                var client = _clients[i];
                var drop = client.OutboundOverflow || (client.CloseAfterFlush && client.OutboundBytes == 0);
                if (!drop && writeList.Contains(client.Sock))
                {
                    drop = !client.TrySend();
                }

                if (!drop && readList.Contains(client.Sock))
                {
                    drop = !ReceiveFrom(client);
                }

                drop = drop || client.OutboundOverflow;
                if (drop)
                {
                    Log($"[relay] client '{client.Name}' disconnected"
                        + (client.OutboundOverflow ? " (outbound queue limit exceeded)" : ""));
                    client.Sock.Close();
                    _clients.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        foreach (var client in _clients)
        {
            client.Sock.Close();
        }

        return 0;
    }

    public RoomProvisionReport ProvisionRoomManifests(IReadOnlyList<string> seedFiles)
    {
        using var lease = new StoreDirectoryLease(_storeDir);
        if (!lease.Acquired)
        {
            return new RoomProvisionReport(false, "",
                "world_state is in use by a relay or another maintenance operation");
        }

        var seeds = new List<Seed>();
        foreach (var file in seedFiles)
        {
            try
            {
                seeds.Add(SeedLoader.LoadFromFile(file));
            }
            catch (Exception ex) when (ex is SeedLoadException or IOException)
            {
                return new RoomProvisionReport(false, "", $"invalid private seed {file}: {ex.Message}");
            }
        }

        if (seeds.Count == 0)
        {
            return new RoomProvisionReport(false, "", "every private player seed is required");
        }

        var first = seeds[0];
        if (seeds.Any(s => s.SeedId != first.SeedId || s.SettingsHash != first.SettingsHash
                           || s.TeamId != first.TeamId || s.PlayerCount != first.PlayerCount
                           || s.SessionEpoch != first.SessionEpoch))
        {
            return new RoomProvisionReport(false, first.SeedId,
                "private seeds disagree on room, settings, team, player count, or epoch");
        }

        if (seeds.Count != first.PlayerCount
            || Enumerable.Range(1, first.PlayerCount).Any(p => seeds.Count(s => s.PlayerId == p) != 1))
        {
            return new RoomProvisionReport(false, first.SeedId,
                $"expected exactly one seed per player 1..{first.PlayerCount}");
        }

        foreach (var seed in seeds)
        {
            var line = SeedManifest.Build(seed);
            var path = V4RoomStore.ManifestPathFor(
                _storeDir, V4RoomStore.ProgressRoom(seed.SeedId, seed.TeamId), seed.PlayerId);
            if (File.Exists(path) && File.ReadAllText(path) != line)
            {
                return new RoomProvisionReport(false, first.SeedId,
                    $"existing manifest conflicts for player {seed.PlayerId}");
            }
        }

        foreach (var seed in seeds)
        {
            if (!DurableFile.WriteAtomic(
                    V4RoomStore.ManifestPathFor(
                        _storeDir, V4RoomStore.ProgressRoom(seed.SeedId, seed.TeamId), seed.PlayerId),
                    SeedManifest.Build(seed)))
            {
                return new RoomProvisionReport(false, first.SeedId,
                    $"cannot durably register manifest for player {seed.PlayerId}");
            }
        }

        return new RoomProvisionReport(true, first.SeedId,
            $"provisioned {seeds.Count} canonical player manifests for room {first.SeedId}");
    }

    private string SessionMetaLine() =>
        $"{{\"type\":\"{RelayProtocol.SessionMeta}\",\"pvp\":{(_rules.Pvp ? "true" : "false")}"
        + $",\"friendlyFire\":{(_rules.FriendlyFire ? "true" : "false")}"
        + $",\"pvpDamage\":{PvpDamage.Clamp(_rules.PvpDamagePercent)}"
        + $",\"deathLinkMode\":\"{DeathLinkModes.Tag(_rules.DeathLink)}\""
        + $",\"timerEnabled\":{(_rules.TimerEnabled ? "true" : "false")}"
        + $",\"timerMin\":{_rules.TimerMinPercent},\"timerMax\":{_rules.TimerMaxPercent}"
        + $",\"traps\":{_rules.Traps.ToMetaJson()}}}\n";

    private bool ReceiveFrom(ClientConn client)
    {
        while (true)
        {
            int received;
            try
            {
                received = client.Sock.Receive(client.RecvChunk, SocketFlags.None);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                return true;
            }
            catch (SocketException)
            {
                return false;
            }

            if (received == 0)
            {
                return false;
            }

            client.Buffer.AddRange(client.RecvChunk.AsSpan(0, received));
            if (client.Buffer.Count > MaxClientReceiveFrameBytes)
            {
                Log("[relay] client receive frame exceeded limit");
                return false;
            }

            int newline;
            while ((newline = client.Buffer.IndexOf((byte)'\n')) >= 0)
            {
                var line = System.Text.Encoding.UTF8.GetString(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(client.Buffer)[..newline]).TrimEnd('\r');
                client.Buffer.RemoveRange(0, newline + 1);
                if (line.Length == 0)
                {
                    continue;
                }

                if (!HandleLine(client, line))
                {
                    client.Buffer.Clear();
                    break;
                }
            }
        }
    }

    private bool HandleLine(ClientConn c, string line)
    {
        var type = RelayProtocol.ExtractStringField(line, "type");
        if (type == RelayProtocol.Hello)
        {
            return HandleHello(c, line);
        }

        if (type == "OOTMM_HANDSHAKE")
        {
            Log($"[relay] refused protocol v2 handshake from "
                + $"'{RelayProtocol.ExtractStringField(line, "clientName")}': protocol v4 required");
            c.QueueWire($"{{\"type\":\"{RelayProtocol.ProtocolError}\",\"reason\":"
                        + "\"multiworld protocol v4 required - update OoTMM PC\"}\n");
            c.CloseAfterFlush = true;
            return false;
        }

        if (c.Room.Length == 0)
        {
            return true;
        }

        if (type == RelayProtocol.Ping)
        {
            c.QueueWire($"{{\"type\":\"{RelayProtocol.Pong}\",\"nonce\":\""
                        + RelayProtocol.ExtractStringField(line, "nonce") + "\"}\n");
            return true;
        }

        if (type == RelayProtocol.SeedManifest)
        {
            HandleManifestUpload(c, line);
            return true;
        }

        if (RelayProtocol.ExtractIntField(line, "v") != 4)
        {
            HandleSideChannel(c, line, type);
            return true;
        }

        if (line.Contains("\"since\"", StringComparison.Ordinal))
        {
            HandleCatchUp(c, line);
            return true;
        }

        HandleFactPublish(c, line);
        return true;
    }

    private bool HandleHello(ClientConn c, string line)
    {
        var protocolVersion = RelayProtocol.ExtractIntField(line, "protocolVersion");
        var clientName = RelayProtocol.ExtractStringField(line, "clientName");
        if (protocolVersion != 4)
        {
            Log($"[relay] refused protocol v{protocolVersion} hello from '{clientName}': v4 required");
            c.QueueWire(StatusError("multiworld protocol v4 required - update OoTMM PC"));
            c.CloseAfterFlush = true;
            return false;
        }

        var room = RelayProtocol.ExtractStringField(line, "seedId");
        var playerId = RelayProtocol.ExtractIntField(line, "playerId");
        var playerCount = RelayProtocol.ExtractIntField(line, "playerCount");
        if (c.Room.Length > 0 || room.Length == 0 || playerId is <= 0 or > 65535
            || playerCount < playerId
            || RelayProtocol.ExtractStringField(line, "format") != Seed.CurrentFormat
            || RelayProtocol.ExtractStringField(line, "generatorVersion") != Seed.RequiredGeneratorVersion)
        {
            c.QueueWire(StatusError("invalid v4 hello"));
            return true;
        }

        var teamId = RelayProtocol.ExtractStringField(line, "teamId");
        var progress = V4RoomStore.ProgressRoom(room, teamId);

        if (File.Exists(V4RoomStore.ResetMarkerPathFor(_storeDir, progress)))
        {
            Log($"[relay] refused v4 hello from '{clientName}': room {progress} has an epoch reset in progress");
            c.QueueWire(StatusError("room epoch reset in progress - joins are disabled until it completes"));
            return true;
        }

        var store = RoomStore(progress);
        if (!store.Load(_storeDir, progress))
        {
            c.QueueWire(StatusError("room store unavailable"));
            return true;
        }

        _progressSeeds[progress] = room;
        c.Room = room;
        c.Progress = progress;
        c.Name = clientName;
        c.PlayerId = playerId;
        c.PlayerCount = playerCount;
        c.SettingsHash = RelayProtocol.ExtractStringField(line, "settingsHash");
        c.TeamId = teamId;
        c.QueueWire($"{{\"type\":\"{RelayProtocol.Ready}\",\"protocolVersion\":4,\"seedId\":"
                    + JsonText.EscapeString(room) + "}\n");
        c.QueueWire(SessionMetaLine());

        var manifestsForwarded = 0;
        for (var player = 1; player <= playerCount; player++)
        {
            if (player == playerId)
            {
                continue;
            }

            var path = V4RoomStore.ManifestPathFor(_storeDir, progress, player);
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path).TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (SeedManifest.Parse(text) is not null && c.QueueWire(text + "\n"))
            {
                manifestsForwarded++;
            }
        }

        Log($"[relay] '{c.Name}' joined v4 room {progress} (player {playerId}); "
            + $"{store.Lines.Count} fact(s) durable, {manifestsForwarded} manifest(s) forwarded");
        return true;
    }

    private void HandleManifestUpload(ClientConn c, string line)
    {
        var requested = SeedManifest.Parse(line);
        var matchesIdentity = requested is not null
                              && requested.Seed.SeedId == c.Room
                              && requested.Seed.PlayerId == c.PlayerId
                              && requested.Seed.PlayerCount == c.PlayerCount
                              && requested.Seed.SettingsHash == c.SettingsHash
                              && requested.Seed.TeamId == c.TeamId;
        if (!matchesIdentity || !RegisterPlayerManifest(requested!, line, out var bootstrapped))
        {
            Log($"[relay] dropped mismatched/unwritable v4 manifest from '{c.Name}'");
            return;
        }

        _manifests.TryAdd(c.Progress, []);
        _manifests[c.Progress][c.PlayerId] = requested!;
        _catalogs.TryAdd(c.Progress, []);
        _catalogs[c.Progress][c.PlayerId] =
            (c.Name, requested!.Seed.Placements.Select(p => p.Check.Id).ToList());

        if (bootstrapped)
        {
            Log($"[relay] registered v4 player manifest for room {c.Progress} player {c.PlayerId}");
            BroadcastProgress(c.Progress, line + "\n", except: c);
        }
    }

    private bool RegisterPlayerManifest(SeedManifestRegistration requested, string rawLine,
                                        out bool bootstrapped)
    {
        bootstrapped = false;
        var path = V4RoomStore.ManifestPathFor(
            _storeDir, V4RoomStore.ProgressRoom(requested.Seed.SeedId, requested.Seed.TeamId),
            requested.Seed.PlayerId);
        if (File.Exists(path))
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            var parsed = SeedManifest.Parse(text);
            return text == rawLine && parsed is not null
                   && parsed.ManifestHash == requested.ManifestHash
                   && parsed.SessionEpoch == requested.SessionEpoch
                   && parsed.Seed.SeedId == requested.Seed.SeedId
                   && parsed.Seed.PlayerId == requested.Seed.PlayerId;
        }

        if (!_rules.AllowManifestBootstrap || !DurableFile.WriteAtomic(path, rawLine))
        {
            return false;
        }

        bootstrapped = true;
        return true;
    }

    private void HandleSideChannel(ClientConn c, string line, string type)
    {
        var sideChannel = type is PresencePackets.PlayerState or PresencePackets.PvpHit
            or PresencePackets.DeathLink or PresencePackets.ModAnnounce
            or PresencePackets.ModRequest or PresencePackets.ModChunk or RelayProtocol.WorldFlag;
        if (!sideChannel)
        {
            Log($"[relay] dropped non-v4 frame '{type}' from v4 client '{c.Name}'");
            return;
        }

        var verified = new VerifiedClientIdentity(
            c.Room, c.SettingsHash, c.TeamId, (ushort)c.PlayerId, (ushort)c.PlayerCount);
        if (!SideChannelPolicy.IsRoomPacketAllowed(line, verified))
        {
            Log($"[relay] dropped unauthorised v4 side-channel frame '{type}' from '{c.Name}'");
            return;
        }

        if (type == PresencePackets.PvpHit && !_rules.Pvp)
        {
            return;
        }

        if (type == PresencePackets.PvpHit)
        {
            Broadcast(c.Room, line + "\n", except: c,
                      recipient => _rules.FriendlyFire || recipient.TeamId != c.TeamId);
            return;
        }

        if (type == PresencePackets.DeathLink)
        {
            if (_rules.DeathLink != DeathLinkMode.Off)
            {
                Broadcast(c.Room, line + "\n", except: c,
                          recipient => _rules.DeathLink == DeathLinkMode.Global || recipient.TeamId == c.TeamId);
            }

            return;
        }

        if (type == RelayProtocol.WorldFlag)
        {
            BroadcastProgress(c.Progress, line + "\n", except: c);
            return;
        }

        Broadcast(c.Room, line + "\n", except: c);
    }

    private void HandleCatchUp(ClientConn c, string line)
    {
        var since = FactLog.DecodeCatchUp(line);
        var store = RoomStore(c.Progress);
        if (since is null || !store.Load(_storeDir, c.Progress))
        {
            Log($"[relay] dropped malformed/unserviceable v4 catch-up from '{c.Name}'");
            return;
        }

        var first = (int)Math.Min(since.Value, (ulong)store.Lines.Count);
        for (var index = first; index < store.Lines.Count; index++)
        {
            c.QueueWire(store.Lines[index] + "\n");
        }

        c.QueueWire(FactLog.EncodeLive() + "\n");
    }

    private void HandleFactPublish(ClientConn c, string line)
    {
        var fact = V4RoomStore.ParseFactLine(line, requireSeq: false);
        if (fact is null)
        {
            Log($"[relay] dropped malformed v4 frame from '{c.Name}'");
            return;
        }

        if (fact.SourcePlayer != c.PlayerId)
        {
            Log($"[relay] dropped v4 fact for player {fact.SourcePlayer} from '{c.Name}' (player {c.PlayerId})");
            return;
        }

        var store = RoomStore(c.Progress);
        if (!store.Load(_storeDir, c.Progress))
        {
            Log("[relay] v4 store reload failed; fact not acknowledged");
            return;
        }

        var key = V4Transport.NaturalKey(fact.SourcePlayer, fact.Game, fact.CheckId);
        if (store.SeqByKey.TryGetValue(key, out var existingSeq))
        {
            c.QueueWire(FactLog.EncodeAck(fact with { Seq = existingSeq }) + "\n");
            return;
        }

        var assigned = fact with { Seq = (ulong)store.Lines.Count + 1 };
        var factLine = FactLog.EncodeFact(assigned);
        if (!store.Append(_storeDir, c.Progress, factLine))
        {
            Log("[relay] durable v4 fact append failed; fact not acknowledged");
            return;
        }

        store.Lines.Add(factLine);
        store.SeqByKey[key] = assigned.Seq;
        c.QueueWire(FactLog.EncodeAck(assigned) + "\n");
        BroadcastProgress(c.Progress, factLine + "\n", except: null);
    }

    private void Broadcast(string room, string wire, ClientConn? except, Func<ClientConn, bool>? accepts = null)
    {
        foreach (var client in _clients)
        {
            if (client.Room == room && client != except && (accepts is null || accepts(client)))
            {
                client.QueueWire(wire);
            }
        }
    }

    private void BroadcastProgress(string progress, string wire, ClientConn? except)
    {
        foreach (var client in _clients)
        {
            if (client.Progress == progress && client != except)
            {
                client.QueueWire(wire);
            }
        }
    }

    private V4RoomStore RoomStore(string room)
    {
        if (!_rooms.TryGetValue(room, out var store))
        {
            store = new V4RoomStore();
            _rooms[room] = store;
        }

        return store;
    }

    private string SeedOf(string progress) => _progressSeeds.GetValueOrDefault(progress, progress);

    private static string StatusError(string message) =>
        $"{{\"type\":\"{RelayProtocol.Status}\",\"level\":\"error\",\"message\":\"{message}\"}}\n";

    private void InjectRoomLine(string room, string line)
    {
        var sent = 0;
        foreach (var client in _clients)
        {
            if (client.Progress == room && client.QueueWire(line + "\n"))
            {
                sent++;
            }
        }

        Log($"[relay] injected into room {room} ({sent} clients): {line}");
    }

    private void RunCommand(string raw)
    {
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        switch (parts[0])
        {
            case "/help":
                Log("[relay] commands:\n"
                    + "  /help                                     this list\n"
                    + "  /list-sessions                            active rooms (session ids)\n"
                    + "  /list-players [session-id]                connected players\n"
                    + "  /list-checks <session-id> [player]        all checks + status\n"
                    + "  /unlock-check <session-id> <who> <check-id>  record a completed check\n"
                    + "  /unlock-all <session-id> <who> [game]     unlock every check for a player\n"
                    + "  /teleport <session-id> <who> <dest>       teleport a player\n"
                    + "  /say <session-id> <message...>            broadcast a chat line to a room");
                return;
            case "/list-sessions":
                ListSessions();
                return;
            case "/list-players":
                foreach (var client in _clients)
                {
                    if (client.Progress.Length > 0 && (parts.Length < 2 || client.Progress == parts[1]))
                    {
                        Log($"  '{client.Name}' in {client.Progress}");
                    }
                }

                return;
            case "/list-checks" when parts.Length >= 2:
                ListChecks(parts[1], parts.Length >= 3 ? parts[2] : "");
                return;
            case "/unlock-check" when parts.Length >= 4:
                UnlockCheck(parts[1], parts[2], parts[3]);
                return;
            case "/unlock-all" when parts.Length >= 3:
            {
                var game = parts.Length >= 4 ? parts[3] : "all";
                if (game is not ("oot" or "mm" or "all"))
                {
                    Log("[relay] game scope must be oot, mm, or all");
                    return;
                }

                InjectRoomLine(parts[1], $"{{\"type\":\"{RelayProtocol.AdminUnlockAll}\",\"seedId\":\"{SeedOf(parts[1])}\""
                                         + $",\"who\":\"{parts[2]}\",\"game\":\"{game}\"}}");
                return;
            }
            case "/teleport" when parts.Length >= 4:
                InjectRoomLine(parts[1], $"{{\"type\":\"{RelayProtocol.Teleport}\",\"seedId\":\"{SeedOf(parts[1])}\""
                                         + $",\"who\":\"{parts[2]}\",\"dest\":\"{parts[3]}\"}}");
                return;
            case "/say" when parts.Length >= 3:
            {
                var message = raw[(raw.IndexOf(parts[1], StringComparison.Ordinal) + parts[1].Length)..].Trim();
                InjectRoomLine(parts[1], $"{{\"type\":\"{RelayProtocol.Chat}\",\"seedId\":\"{SeedOf(parts[1])}\""
                                         + $",\"from\":\"server\",\"text\":{JsonText.EscapeString(message)}}}");
                return;
            }
            default:
                Log($"[relay] unknown command '{parts[0]}' — /help lists commands");
                return;
        }
    }

    private void ListSessions()
    {
        var active = new SortedSet<string>(_rooms.Keys);
        foreach (var client in _clients)
        {
            if (client.Progress.Length > 0)
            {
                active.Add(client.Progress);
            }
        }

        Log($"[relay] {active.Count} session(s):");
        foreach (var room in active)
        {
            var online = _clients.Count(c => c.Progress == room);
            var stored = _rooms.TryGetValue(room, out var store) ? store.Lines.Count : 0;
            Log($"  {room}  ({online} online, {stored} stored facts)");
        }
    }

    private void ListChecks(string room, string playerArg)
    {
        var store = RoomStore(room);
        if (!store.Load(_storeDir, room))
        {
            Log($"[relay] cannot read durable store for {room}");
            return;
        }

        var completedByPlayer = new Dictionary<int, HashSet<string>>();
        foreach (var line in store.Lines)
        {
            if (V4RoomStore.ParseFactLine(line, requireSeq: true) is { } fact)
            {
                completedByPlayer.TryAdd(fact.SourcePlayer, []);
                completedByPlayer[fact.SourcePlayer].Add(fact.CheckId);
            }
        }

        var roomCatalogs = _catalogs.GetValueOrDefault(room) ?? [];
        var filter = ResolvePlayerArg(room, playerArg, roomCatalogs);
        if (playerArg.Length > 0 && filter == 0)
        {
            Log($"[relay] no player named '{playerArg}' in {room}");
            return;
        }

        if (filter == 0)
        {
            foreach (var (pid, cat) in roomCatalogs)
            {
                var done = completedByPlayer.GetValueOrDefault(pid)?.Count ?? 0;
                Log($"  player {pid} ('{cat.Name}'): {done}/{cat.CheckIds.Count} checks");
            }

            if (roomCatalogs.Count == 0)
            {
                foreach (var (pid, done) in completedByPlayer)
                {
                    Log($"  player {pid}: {done.Count} completed");
                }
            }

            return;
        }

        var doneSet = completedByPlayer.GetValueOrDefault(filter) ?? [];
        if (!roomCatalogs.TryGetValue(filter, out var catalog))
        {
            Log($"[relay] no catalog for player {filter} in {room} — showing {doneSet.Count} stored completion(s):");
            foreach (var id in doneSet.Order())
            {
                Log($"  [x] {id}");
            }

            return;
        }

        var completed = 0;
        foreach (var id in catalog.CheckIds)
        {
            var got = doneSet.Contains(id);
            completed += got ? 1 : 0;
            Log($"  {(got ? "[x] " : "[ ] ")}{id}");
        }

        Log($"[relay] player {filter} ('{catalog.Name}'): {completed}/{catalog.CheckIds.Count} checks in {room}");
    }

    private void UnlockCheck(string room, string who, string checkId)
    {
        var store = RoomStore(room);
        if (!store.Load(_storeDir, room))
        {
            Log($"[relay] cannot read durable store for {room}");
            return;
        }

        var targets = new SortedSet<int>();
        if (who == "all")
        {
            foreach (var client in _clients)
            {
                if (client.Progress == room && client.PlayerId > 0)
                {
                    targets.Add(client.PlayerId);
                }
            }
        }
        else
        {
            var resolved = ResolvePlayerArg(room, who, _catalogs.GetValueOrDefault(room) ?? []);
            if (resolved > 0)
            {
                targets.Add(resolved);
            }
        }

        if (targets.Count == 0)
        {
            Log("[relay] unlock-check requires the addressed player to be connected and verified");
            return;
        }

        var dispatched = 0;
        var alreadyCommitted = 0;
        foreach (var player in targets)
        {
            var manifest = _manifests.GetValueOrDefault(room)?.GetValueOrDefault(player);
            if (manifest is null)
            {
                Log($"[relay] player {player} has no verified live manifest");
                continue;
            }

            var matches = manifest.Seed.Placements.Where(p => p.Check.Id == checkId).ToList();
            if (matches.Count != 1)
            {
                Log($"[relay] check '{checkId}' is absent or ambiguous for player {player}");
                continue;
            }

            var key = V4Transport.NaturalKey((ushort)player, matches[0].Check.Game, matches[0].Check.Id);
            if (store.SeqByKey.ContainsKey(key))
            {
                alreadyCommitted++;
                continue;
            }

            var commandId = "admin-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            InjectRoomLine(room, $"{{\"type\":\"{RelayProtocol.AdminUnlockCheck}\",\"seedId\":\"{SeedOf(room)}\""
                                 + $",\"who\":\"player-id={player}\",\"checkId\":\"{matches[0].Check.Id}\""
                                 + $",\"commandId\":\"{commandId}\",\"grant\":false}}");
            dispatched++;
        }

        Log($"[relay] unlock-check {checkId} dispatched to {dispatched} player(s); "
            + $"{alreadyCommitted} already durably committed");
    }

    private int ResolvePlayerArg(string room, string arg,
                                 Dictionary<int, (string Name, List<string> CheckIds)> roomCatalogs)
    {
        if (arg.Length == 0)
        {
            return 0;
        }

        if (arg.StartsWith("player-id=", StringComparison.Ordinal)
            && int.TryParse(arg.AsSpan(10), out var explicitId))
        {
            return explicitId;
        }

        if (int.TryParse(arg, out var bareId))
        {
            return bareId;
        }

        foreach (var (pid, cat) in roomCatalogs)
        {
            if (cat.Name == arg)
            {
                return pid;
            }
        }

        foreach (var client in _clients)
        {
            if (client.Progress == room && client.Name == arg)
            {
                return client.PlayerId;
            }
        }

        return 0;
    }

    private sealed class ClientConn(Socket sock)
    {
        public Socket Sock { get; } = sock;
        public List<byte> Buffer { get; } = [];
        public byte[] RecvChunk { get; } = new byte[4096];
        public string Room { get; set; } = "";
        public string Progress { get; set; } = "";
        public string Name { get; set; } = "";
        public int PlayerId { get; set; }
        public int PlayerCount { get; set; }
        public string SettingsHash { get; set; } = "";
        public string TeamId { get; set; } = "";
        public bool CloseAfterFlush { get; set; }
        public bool OutboundOverflow { get; private set; }
        public int OutboundBytes { get; private set; }

        private readonly Queue<byte[]> _outbound = [];
        private int _headOffset;

        public bool QueueWire(string wire)
        {
            if (OutboundOverflow)
            {
                return false;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(wire);
            if (bytes.Length > MaxClientOutboundBytes || OutboundBytes > MaxClientOutboundBytes - bytes.Length)
            {
                OutboundOverflow = true;
                return false;
            }

            OutboundBytes += bytes.Length;
            _outbound.Enqueue(bytes);
            return true;
        }

        public bool TrySend()
        {
            while (_outbound.TryPeek(out var head))
            {
                int sent;
                try
                {
                    sent = Sock.Send(head, _headOffset, head.Length - _headOffset, SocketFlags.None);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    return true;
                }
                catch (SocketException)
                {
                    return false;
                }

                if (sent <= 0)
                {
                    return false;
                }

                _headOffset += sent;
                OutboundBytes -= sent;
                if (_headOffset == head.Length)
                {
                    _outbound.Dequeue();
                    _headOffset = 0;
                }
            }

            return true;
        }
    }
}
