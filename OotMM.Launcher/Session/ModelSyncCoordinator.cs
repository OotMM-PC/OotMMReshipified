using OotMM.Core;
using OotMM.Core.Models;
using OotMM.Core.Relay;
using OotMM.Launcher.Models;

namespace OotMM.Launcher.Session;

/// <summary>One game's shareable archives in repack order: the LAST path wins collisions,
/// mirroring how the archives mount on the owner's machine.</summary>
public sealed record ModelShareSet(Game Game, IReadOnlyList<string> ArchivePaths);

/// <summary>
/// Ships every player's model archives around the session and installs each remote set as a
/// namespaced zz_coop archive, so puppets can wear their own player's model. Transfers ride the
/// relay side channel; installed archives mount at that game's next launch.
/// </summary>
public sealed class ModelSyncCoordinator
{
    private const int ChunkLinesPerPump = 4;
    private const int MaxHashFailures = 3;
    // Rides count-0 announces; never a real content hash, never requested or cached.
    private const string EmptySetHash =
        "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly TimeSpan AnnounceEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadStall = TimeSpan.FromSeconds(20);

    private sealed class OwnArchive(ModelAnnounce announce, string path)
    {
        public ModelAnnounce Announce { get; } = announce;
        public string Path { get; } = path;
    }

    private sealed class RemoteSet(int count)
    {
        public int Count { get; } = count;
        public Dictionary<int, ModelAnnounce> ByIndex { get; } = [];
        public bool Refused { get; set; }
        public string FailedSignature { get; set; } = "";
    }

    private sealed class ChunkStream(string hash, string path, long total)
    {
        public string Hash { get; } = hash;
        public string Path { get; } = path;
        public long Total { get; } = total;
        public long Next { get; set; }
    }

    private readonly Seed _seed;
    private readonly string _cacheDir;
    private readonly int _maxMegabytes;
    private readonly IReadOnlyList<ModelShareSet> _shares;
    private readonly IReadOnlyDictionary<Game, string> _modsDirs;
    private readonly IReadOnlyDictionary<Game, IReadOnlyList<string>> _fillArchives;
    private readonly Func<string, bool> _send;

    private List<OwnArchive>? _own;
    private readonly List<ChunkStream> _streams = [];
    private readonly Dictionary<string, ModelDownload> _downloads = [];
    private readonly Dictionary<string, TimeSpan> _downloadProgressAt = [];
    private readonly Dictionary<string, TimeSpan> _requestedAt = [];
    private readonly Dictionary<string, int> _hashFailures = [];
    private readonly Dictionary<(int Player, Game Game), RemoteSet> _remoteSets = [];
    private readonly Dictionary<(int Player, Game Game), string> _installed = [];
    private readonly Dictionary<(int Player, Game Game), TimeSpan> _installRetryAt = [];
    private readonly HashSet<string> _installBlockedLogged = [];
    private TimeSpan _lastAnnounceAt = TimeSpan.MinValue;
    private ulong _announcedGeneration;
    private bool _fillsBuilt;
    private Game? _runningGame;

    private static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public ModelSyncCoordinator(
        Seed seed, string cacheDir, int maxMegabytes, IReadOnlyList<ModelShareSet> shares,
        IReadOnlyDictionary<Game, string> modsDirs,
        IReadOnlyDictionary<Game, IReadOnlyList<string>> fillArchives, Func<string, bool> send)
    {
        _seed = seed;
        _cacheDir = cacheDir;
        _maxMegabytes = maxMegabytes;
        _shares = shares;
        _modsDirs = modsDirs;
        _fillArchives = fillArchives;
        _send = send;
        // Sets learned in earlier sessions install NOW, before the games boot and lock their
        // archives; a hard-killed launcher must not lose what a session already received.
        LoadPersistedSets();
        TryInstallAll();
    }

    public event Action<string>? Logged;

    /// <summary>Builds the session's coordinator from the saved form. The MM share set carries
    /// the ported OoT archives automatically, so other players see this player's ported human
    /// form without selecting anything.</summary>
    public static ModelSyncCoordinator Create(
        Core.Session session, UiSettings settings, Func<string, bool> send)
    {
        var ootMods = ModsDir(session.OotExe);
        var mmMods = ModsDir(session.MmExe);

        // Pick lists read top-priority-first; the repacker gives collisions to the LAST input.
        var ootShare = settings.PlayerModelOot.AsEnumerable().Reverse()
            .Select(name => Path.Combine(ootMods, name))
            .Where(File.Exists)
            .ToList();
        var mmShare = settings.PlayerModelMm.AsEnumerable().Reverse()
            .Select(name => Path.Combine(mmMods, name))
            .Where(File.Exists)
            .ToList();
        mmShare.AddRange(new[]
            {
                Path.Combine(mmMods, LocalModelPort.AdultArchive),
                Path.Combine(mmMods, LocalModelPort.ChildArchive),
            }
            .Where(File.Exists));

        var ootExeDir = Path.GetDirectoryName(session.OotExe) ?? ".";
        var mmExeDir = Path.GetDirectoryName(session.MmExe) ?? ".";
        return new ModelSyncCoordinator(
            session.Seed,
            Path.Combine(session.SessionDir, "models"),
            settings.MaxModelSyncMb,
            [new ModelShareSet(Game.Oot, ootShare), new ModelShareSet(Game.Mm, mmShare)],
            new Dictionary<Game, string> { [Game.Oot] = ootMods, [Game.Mm] = mmMods },
            new Dictionary<Game, IReadOnlyList<string>>
            {
                [Game.Oot] = [Path.Combine(ootExeDir, "oot.o2r")],
                [Game.Mm] = [Path.Combine(mmExeDir, "mm.o2r"), Path.Combine(mmMods, "ootmm_assets.o2r")],
            },
            send);
    }

    public void HandleLine(string line)
    {
        switch (RelayProtocol.ExtractStringField(line, "type"))
        {
            case PresencePackets.ModAnnounce:
                if (ModelSyncProtocol.ParseAnnounce(line) is { } announce)
                {
                    HandleAnnounce(announce);
                }

                break;
            case PresencePackets.ModRequest:
                if (ModelSyncProtocol.ParseRequest(line) is { } request)
                {
                    HandleRequest(request);
                }

                break;
            case PresencePackets.ModChunk:
                if (ModelSyncProtocol.ParseChunk(line) is { } chunk)
                {
                    HandleChunk(chunk);
                }

                break;
        }
    }

    public void Tick(bool ready, ulong generation, Game? runningGame = null)
    {
        // The running game holds its mounted zz_coop archive locked; its installs wait
        // until it stops (a cross-game handoff, or InstallNow at session end).
        _runningGame = runningGame;
        BuildFillsOnce();
        if (!ready)
        {
            return;
        }

        _own ??= BuildOwnArchives();
        var now = Now;
        // Announces are ephemeral: they repeat so reconnects and late joiners still hear them.
        if (generation != _announcedGeneration || _lastAnnounceAt == TimeSpan.MinValue
            || now - _lastAnnounceAt >= AnnounceEvery)
        {
            _announcedGeneration = generation;
            _lastAnnounceAt = now;
            foreach (var own in _own)
            {
                _send(ModelSyncProtocol.BuildAnnounce(_seed, own.Announce));
            }
        }

        foreach (var stalled in _downloads.Keys
                     .Where(hash => now - _downloadProgressAt.GetValueOrDefault(hash) > DownloadStall)
                     .ToList())
        {
            _downloads.Remove(stalled);
            _downloadProgressAt.Remove(stalled);
        }

        foreach (var (key, set) in _remoteSets)
        {
            if (set.Refused)
            {
                continue;
            }

            if (!ModelSyncPolicy.WithinCap(set.ByIndex.Values.Sum(a => a.Size), _maxMegabytes))
            {
                set.Refused = true;
                Logged?.Invoke($"Player {key.Player}'s {GameInfo.ToSeedTag(key.Game)} model set refused: "
                               + ModelSyncPolicy.DescribeRefusal(
                                   set.ByIndex.Values.Sum(a => a.Size), _maxMegabytes)
                               + "; their puppet keeps the vanilla model.");
                continue;
            }

            foreach (var announce in set.ByIndex.Values)
            {
                if (File.Exists(CachePath(announce.Hash)) || _downloads.ContainsKey(announce.Hash)
                    || _hashFailures.GetValueOrDefault(announce.Hash) >= MaxHashFailures)
                {
                    continue;
                }

                if (_requestedAt.TryGetValue(announce.Hash, out var at) && now - at < RequestSpacing)
                {
                    continue;
                }

                _requestedAt[announce.Hash] = now;
                _send(ModelSyncProtocol.BuildRequest(
                    _seed, new ModelRequest(_seed.PlayerId, announce.PlayerId, announce.Hash)));
            }
        }

        TryInstallAll();
    }

    /// <summary>Feeds the active chunk stream into the relay queue; called at presence rate so
    /// serving is paced by the queue's own backpressure rather than the slow session tick.</summary>
    public void PumpSend(bool ready)
    {
        if (!ready || _streams.Count == 0)
        {
            return;
        }

        var stream = _streams[0];
        try
        {
            using var file = File.OpenRead(stream.Path);
            for (var i = 0; i < ChunkLinesPerPump; i++)
            {
                file.Seek(stream.Next * ModelSyncProtocol.ChunkBytes, SeekOrigin.Begin);
                var data = new byte[ModelSyncProtocol.ChunkBytes];
                var read = file.Read(data, 0, data.Length);
                if (read <= 0)
                {
                    _streams.RemoveAt(0);
                    return;
                }

                var line = ModelSyncProtocol.BuildChunk(_seed, new ModelChunk(
                    _seed.PlayerId, stream.Hash, stream.Next, stream.Total, data.AsSpan(0, read).ToArray()));
                if (!_send(line))
                {
                    return; // queue full: resume from the same chunk next pump
                }

                stream.Next++;
                if (stream.Next >= stream.Total)
                {
                    _streams.RemoveAt(0);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _streams.RemoveAt(0);
            Logged?.Invoke($"Stopped serving a model archive: {ex.Message}");
        }
    }

    private void HandleAnnounce(ModelAnnounce announce)
    {
        if (announce.PlayerId == _seed.PlayerId)
        {
            return;
        }

        var key = (announce.PlayerId, announce.Game);
        if (!_remoteSets.TryGetValue(key, out var set) || set.Count != announce.Count)
        {
            set = new RemoteSet(announce.Count);
            _remoteSets[key] = set;
        }

        if (announce.Count == 0)
        {
            PersistSets(); // an empty set is complete by existing; the install turns it into fill
            return;
        }

        if (set.ByIndex.TryGetValue(announce.Index, out var existing) && existing.Hash == announce.Hash)
        {
            return;
        }

        set.ByIndex[announce.Index] = announce;
        set.Refused = false;
        if (set.ByIndex.Count == set.Count)
        {
            PersistSets();
        }
    }

    private void HandleRequest(ModelRequest request)
    {
        if (request.PlayerId == _seed.PlayerId || request.TargetPlayer != _seed.PlayerId
            || _own is null)
        {
            return;
        }

        if (_own.FirstOrDefault(o => o.Announce.Hash == request.Hash) is not { } own
            || own.Announce.Count == 0)
        {
            return;
        }

        // A repeat request means the receiver dropped its partial download: restart from zero.
        if (_streams.FirstOrDefault(s => s.Hash == request.Hash) is { } active)
        {
            active.Next = 0;
            return;
        }

        _streams.Add(new ChunkStream(request.Hash, own.Path, TotalChunks(own.Announce.Size)));
        Logged?.Invoke($"Sending {own.Announce.Name} to player {request.PlayerId}.");
    }

    private void HandleChunk(ModelChunk chunk)
    {
        if (chunk.PlayerId == _seed.PlayerId)
        {
            return;
        }

        if (!_downloads.TryGetValue(chunk.Hash, out var download))
        {
            // Chunks broadcast to the room, so another player's request can start ours for free.
            if (chunk.Seq != 0 || File.Exists(CachePath(chunk.Hash)) || !IsWanted(chunk.Hash))
            {
                return;
            }

            download = new ModelDownload(chunk.Hash, chunk.Total);
            _downloads[chunk.Hash] = download;
        }

        if (!download.Accept(chunk))
        {
            return;
        }

        _downloadProgressAt[chunk.Hash] = Now;
        if (!download.Complete)
        {
            return;
        }

        _downloads.Remove(chunk.Hash);
        _downloadProgressAt.Remove(chunk.Hash);
        var bytes = download.Assemble();
        if (ModelSyncProtocol.HashBytes(bytes) != chunk.Hash)
        {
            var failures = _hashFailures.GetValueOrDefault(chunk.Hash) + 1;
            _hashFailures[chunk.Hash] = failures;
            Logged?.Invoke(failures >= MaxHashFailures
                ? "A model archive kept failing verification; giving up on it."
                : "A model archive failed verification; it will be requested again.");
            return;
        }

        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = CachePath(chunk.Hash);
            File.WriteAllBytes(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logged?.Invoke($"Could not cache a model archive: {ex.Message}");
            return;
        }

        TryInstallAll();
    }

    /// <summary>Install pass for session teardown: the games are gone, so every pending
    /// set can land and mount at the next launch.</summary>
    public void InstallNow()
    {
        _runningGame = null;
        _installRetryAt.Clear();
        TryInstallAll();
    }

    private void TryInstallAll()
    {
        foreach (var ((player, game), set) in _remoteSets)
        {
            if (game == _runningGame || set.Refused || set.ByIndex.Count != set.Count)
            {
                continue;
            }

            var ordered = Enumerable.Range(0, set.Count)
                .Select(i => set.ByIndex.GetValueOrDefault(i))
                .ToList();
            if (ordered.Any(a => a is null || !File.Exists(CachePath(a.Hash))))
            {
                continue;
            }

            // The set composes OVER the vanilla base: an asset the sender lacks must resolve
            // to namespaced vanilla, never fall through to THIS client's canonical mods.
            var bases = _fillArchives[game].Where(File.Exists).ToList();
            var signature = "set|" + string.Join("|", ordered.Select(a => a!.Hash))
                            + "|base|" + string.Join("|", bases.Select(path =>
                            {
                                var info = new FileInfo(path);
                                return $"{Path.GetFileName(path)},{info.Length},{info.LastWriteTimeUtc.Ticks}";
                            }));
            if (_installed.GetValueOrDefault((player, game)) == signature
                || set.FailedSignature == signature
                || (_installRetryAt.TryGetValue((player, game), out var retryAt) && Now < retryAt))
            {
                continue;
            }

            var output = OutputPath(game, player);
            var sidecar = output + ".sources";
            try
            {
                if (File.Exists(output) && File.Exists(sidecar) && File.ReadAllText(sidecar) == signature)
                {
                    _installed[(player, game)] = signature;
                    continue;
                }

                var result = ModelRepack.Repack(
                    bases.Concat(ordered.Select(a => CachePath(a!.Hash))).ToList(), output, player);
                if (!result.Ok)
                {
                    set.FailedSignature = signature;
                    Logged?.Invoke($"Could not install player {player}'s "
                                   + $"{GameInfo.ToSeedTag(game)} model: {result.Error}");
                    continue;
                }

                File.WriteAllText(sidecar, signature);
                _installed[(player, game)] = signature;
                _installRetryAt.Remove((player, game));
                Logged?.Invoke($"Installed player {player}'s {GameInfo.ToSeedTag(game)} model "
                               + $"({result.Kept} assets); it applies at that game's next launch.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Usually the running game holding the mounted archive; retry after it closes.
                _installRetryAt[(player, game)] = Now + TimeSpan.FromSeconds(30);
                if (_installBlockedLogged.Add(signature))
                {
                    Logged?.Invoke($"Could not install player {player}'s model yet ({ex.Message}); "
                                   + "it will keep retrying in the background.");
                }
            }
        }
    }

    // Every other player gets a vanilla-filled namespace up front, so a puppet with no synced
    // model resolves to vanilla instead of falling through to the local player's own assets.
    private void BuildFillsOnce()
    {
        if (_fillsBuilt)
        {
            return;
        }

        _fillsBuilt = true;
        for (var player = 1; player <= _seed.PlayerCount; player++)
        {
            if (player == _seed.PlayerId)
            {
                continue;
            }

            foreach (var (game, fillArchives) in _fillArchives)
            {
                var bases = fillArchives.Where(File.Exists).ToList();
                if (bases.Count == 0)
                {
                    continue;
                }

                var output = OutputPath(game, player);
                var sidecar = output + ".sources";
                var signature = "fill|" + string.Join("\n", bases.Select(path =>
                {
                    var info = new FileInfo(path);
                    return $"{Path.GetFileName(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                }));
                try
                {
                    if (File.Exists(output) && File.Exists(sidecar))
                    {
                        var existing = File.ReadAllText(sidecar);
                        // A synced set from an earlier session outranks a fill; keep it.
                        if (!existing.StartsWith("fill|", StringComparison.Ordinal)
                            || existing == signature)
                        {
                            continue;
                        }
                    }

                    if (ModelRepack.RepackVanillaFill(bases, output, player).Ok)
                    {
                        File.WriteAllText(sidecar, signature);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logged?.Invoke($"Could not prepare player {player}'s vanilla model fill: {ex.Message}");
                }
            }
        }
    }

    private List<OwnArchive> BuildOwnArchives()
    {
        var own = new List<OwnArchive>();
        foreach (var share in _shares)
        {
            // The announced count must match what actually hashed, or receivers
            // would wait forever for an index that never got announced.
            var loaded = new List<(string Path, string Hash, long Size)>();
            foreach (var path in share.ArchivePaths.Where(File.Exists))
            {
                if (!ModelRepack.IsReadableArchive(path))
                {
                    Logged?.Invoke($"{Path.GetFileName(path)} is not a readable archive; "
                                   + "it is not shared.");
                    continue;
                }

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    loaded.Add((path, ModelSyncProtocol.HashBytes(bytes), bytes.Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logged?.Invoke($"Cannot share {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            if (loaded.Count == 0)
            {
                // Announced so receivers retire any set of ours they installed before.
                own.Add(new OwnArchive(
                    new ModelAnnounce(_seed.PlayerId, share.Game, "", EmptySetHash, 1, 0, 0), ""));
                continue;
            }

            for (var i = 0; i < loaded.Count; i++)
            {
                own.Add(new OwnArchive(
                    new ModelAnnounce(_seed.PlayerId, share.Game, Path.GetFileName(loaded[i].Path),
                                      loaded[i].Hash, loaded[i].Size, i, loaded.Count),
                    loaded[i].Path));
            }
        }

        return own;
    }

    private sealed record PersistedSet(int Player, string Game, List<string> Hashes, List<long> Sizes);

    private string SetsPath => Path.Combine(_cacheDir, "sets.json");

    private void PersistSets()
    {
        var complete = _remoteSets
            .Where(kv => kv.Value.ByIndex.Count == kv.Value.Count)
            .Select(kv => new PersistedSet(
                kv.Key.Player, GameInfo.ToSeedTag(kv.Key.Game),
                Enumerable.Range(0, kv.Value.Count).Select(i => kv.Value.ByIndex[i].Hash).ToList(),
                Enumerable.Range(0, kv.Value.Count).Select(i => kv.Value.ByIndex[i].Size).ToList()))
            .ToList();
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(SetsPath, System.Text.Json.JsonSerializer.Serialize(complete));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logged?.Invoke($"Could not persist the model-set table: {ex.Message}");
        }
    }

    private void LoadPersistedSets()
    {
        try
        {
            if (!File.Exists(SetsPath))
            {
                return;
            }

            var sets = System.Text.Json.JsonSerializer.Deserialize<List<PersistedSet>>(
                File.ReadAllText(SetsPath)) ?? [];
            foreach (var persisted in sets)
            {
                if (GameInfo.TryFromSeedTag(persisted.Game) is not { } game
                    || persisted.Player == _seed.PlayerId
                    || persisted.Hashes.Count != persisted.Sizes.Count)
                {
                    continue;
                }

                var set = new RemoteSet(persisted.Hashes.Count);
                for (var i = 0; i < persisted.Hashes.Count; i++)
                {
                    set.ByIndex[i] = new ModelAnnounce(persisted.Player, game, "",
                        persisted.Hashes[i], persisted.Sizes[i], i, persisted.Hashes.Count);
                }

                _remoteSets[(persisted.Player, game)] = set;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException)
        {
            Logged?.Invoke($"Could not read the model-set table: {ex.Message}");
        }
    }

    private bool IsWanted(string hash) =>
        _remoteSets.Values.Any(set => !set.Refused && set.ByIndex.Values.Any(a => a.Hash == hash));

    private string CachePath(string hash) => Path.Combine(_cacheDir, hash + ".bin");

    private string OutputPath(Game game, int player) =>
        Path.Combine(_modsDirs[game], $"zz_coop_{ModelRepack.PlayerPrefix(player)}.o2r");

    private static long TotalChunks(long size) =>
        (size + ModelSyncProtocol.ChunkBytes - 1) / ModelSyncProtocol.ChunkBytes;

    private static string ModsDir(string exePath) =>
        Path.Combine(Path.GetDirectoryName(exePath) ?? ".", "mods");
}
