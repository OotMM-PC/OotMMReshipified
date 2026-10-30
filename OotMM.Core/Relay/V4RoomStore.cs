namespace OotMM.Core.Relay;

public sealed class V4RoomStore
{
    public List<string> Lines { get; private set; } = [];
    public Dictionary<string, ulong> SeqByKey { get; private set; } = [];
    public bool Loaded { get; private set; }

    private bool _syncOnLoad;

    public static string SafeRoomName(string room) =>
        string.Concat(room.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    public static string ProgressRoom(string seedId, string teamId) =>
        teamId.Length == 0 || teamId == "default" || teamId.All(c => c == '0') ? seedId : seedId + "~" + teamId;

    public static string PathFor(string dir, string room) =>
        Path.Combine(dir, $"room_{SafeRoomName(room)}.v4.jsonl");

    public static string ResetMarkerPathFor(string dir, string room) =>
        Path.Combine(dir, $"room_{SafeRoomName(room)}.reset-in-progress.json");

    public static string ManifestPathFor(string dir, string room, int playerId) =>
        Path.Combine(dir, $"room_{SafeRoomName(room)}_p{playerId}.manifest.json");

    public static bool IsSafeCheckId(string value) =>
        value.Length is > 0 and <= 256
        && value.All(ch => ch is >= (char)0x20 and <= (char)0x7e and not '"' and not '\\');

    public static Fact? ParseFactLine(string line, bool requireSeq)
    {
        var fact = FactLog.DecodeFact(line);
        if (fact is null || !IsSafeCheckId(fact.CheckId)
            || (requireSeq ? fact.Seq == 0 : fact.Seq != 0))
        {
            return null;
        }

        return fact;
    }

    public bool Load(string dir, string room)
    {
        if (Loaded)
        {
            return true;
        }

        var path = PathFor(dir, room);
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                Lines = [];
                SeqByKey = [];
                _syncOnLoad = false;
                Loaded = true;
                return true;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var loadedLines = new List<string>();
        var loadedKeys = new Dictionary<string, ulong>();
        var needsRepair = false;
        var start = 0;
        while (start < bytes.Length)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n', start);
            if (newline < 0)
            {
                needsRepair = true;
                break;
            }

            var line = System.Text.Encoding.UTF8.GetString(bytes, start, newline - start).TrimEnd('\r');
            start = newline + 1;
            var fact = ParseFactLine(line, requireSeq: true);
            if (fact is null || fact.Seq != (ulong)loadedLines.Count + 1
                || !loadedKeys.TryAdd(V4Transport.NaturalKey(fact.SourcePlayer, fact.Game, fact.CheckId), fact.Seq))
            {
                return false;
            }

            loadedLines.Add(line);
        }

        if (needsRepair && !Rewrite(dir, room, loadedLines))
        {
            _syncOnLoad = true;
            return false;
        }

        if (!needsRepair && _syncOnLoad && !SyncFile(path))
        {
            return false;
        }

        Lines = loadedLines;
        SeqByKey = loadedKeys;
        _syncOnLoad = false;
        Loaded = true;
        return true;
    }

    public bool Append(string dir, string room, string factLine)
    {
        if (!Loaded)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(dir);
            using var stream = new FileStream(PathFor(dir, room), FileMode.Append, FileAccess.Write);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(factLine + "\n"));
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Invalidate();
            return false;
        }
    }

    public void Invalidate()
    {
        Lines = [];
        SeqByKey = [];
        Loaded = false;
        _syncOnLoad = true;
    }

    public static bool Rewrite(string dir, string room, IReadOnlyList<string> lines)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return DurableFile.WriteAtomic(PathFor(dir, room),
            lines.Count == 0 ? "" : string.Join("\n", lines) + "\n");
    }

    private static bool SyncFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
