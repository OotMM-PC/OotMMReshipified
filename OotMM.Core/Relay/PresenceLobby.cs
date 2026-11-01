namespace OotMM.Core.Relay;

public sealed class PresenceLobby
{
    public const string LobbyField = "lobby";
    public const string TargetLobbyField = "targetLobby";
    public const int MaxSlot = 255;

    private readonly Seed _seed;
    private readonly string _tablePath;
    private readonly Dictionary<string, int> _extraSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (string Lobby, int Player)> _bySlot = [];
    private readonly Action<string> _log;

    public PresenceLobby(Seed seed, string sessionDir, Action<string> log)
    {
        _seed = seed;
        _log = log;
        _tablePath = Path.Combine(sessionDir, "lobby.v1");
        var key = "";
        try
        {
            if (File.Exists(_tablePath))
            {
                var lines = File.ReadAllLines(_tablePath);
                key = lines.Length > 0 ? lines[0].Trim() : "";
                foreach (var row in lines.Skip(1))
                {
                    var parts = row.Split(' ');
                    if (parts.Length == 2 && parts[0].Length > 0 && int.TryParse(parts[1], out var slot)
                        && IsExtraSlot(slot) && !_extraSlots.ContainsValue(slot))
                    {
                        _extraSlots[parts[0]] = slot;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _extraSlots.Clear();
            _log($"Could not read the lobby table, so other clients get fresh puppet slots: {ex.Message}");
        }

        if (key.Length == 0 || key.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            key = Guid.NewGuid().ToString("N");
            _extraSlots.Clear();
        }

        OwnKey = key;
        Save();
    }

    public string OwnKey { get; }

    public IEnumerable<int> RemoteSlots =>
        Enumerable.Range(1, _seed.PlayerCount).Where(p => p != _seed.PlayerId)
            .Concat(_extraSlots.Values).Distinct().Order();

    public string? Localize(string line)
    {
        var lobby = RelayProtocol.ExtractStringField(line, LobbyField);
        if (lobby.Length == 0 || lobby == OwnKey)
        {
            return null;
        }

        var target = RelayProtocol.ExtractStringField(line, TargetLobbyField);
        if (target.Length > 0 && target != OwnKey)
        {
            return null;
        }

        var player = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        var slot = SlotFor(lobby, RelayProtocol.ExtractStringField(line, "teamId"), player);
        if (slot == 0)
        {
            return null;
        }

        line = RelayProtocol.ReplaceIntField(line, "sourcePlayer", slot);
        return target.Length > 0 ? RelayProtocol.ReplaceIntField(line, "targetPlayer", _seed.PlayerId) : line;
    }

    public string? Globalize(string line, bool targeted)
    {
        if (targeted)
        {
            var slot = RelayProtocol.ExtractIntField(line, "targetPlayer");
            if (!_bySlot.TryGetValue(slot, out var remote))
            {
                return null;
            }

            line = RelayProtocol.AppendStringField(
                RelayProtocol.ReplaceIntField(line, "targetPlayer", remote.Player), TargetLobbyField, remote.Lobby);
        }

        return RelayProtocol.AppendStringField(line, LobbyField, OwnKey);
    }

    private int SlotFor(string lobby, string teamId, int player)
    {
        if (player <= 0 || player > _seed.PlayerCount)
        {
            return 0;
        }

        if (teamId == _seed.TeamId && player != _seed.PlayerId && !_extraSlots.ContainsKey(lobby)
            && (!_bySlot.TryGetValue(player, out var holder) || holder.Lobby == lobby))
        {
            _bySlot[player] = (lobby, player);
            return player;
        }

        if (!_extraSlots.TryGetValue(lobby, out var slot))
        {
            slot = Enumerable.Range(_seed.PlayerCount + 1, Math.Max(0, MaxSlot - _seed.PlayerCount))
                .FirstOrDefault(s => IsExtraSlot(s) && !_extraSlots.ContainsValue(s));
            if (slot == 0)
            {
                return 0;
            }

            _extraSlots[lobby] = slot;
            Save();
        }

        _bySlot[slot] = (lobby, player);
        return slot;
    }

    private bool IsExtraSlot(int slot) => slot > _seed.PlayerCount && slot <= MaxSlot;

    private void Save()
    {
        try
        {
            DurableFile.WriteAtomic(_tablePath,
                string.Join('\n', _extraSlots.Select(e => $"{e.Key} {e.Value}").Prepend(OwnKey)) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Could not save the lobby table; puppet slots may change next session: {ex.Message}");
        }
    }
}
