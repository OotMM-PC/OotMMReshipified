using System.IO.Compression;
using System.Text;

namespace OotMM.Core.Models;

public sealed record RepackResult(bool Ok, int Kept, int StringRefs, int HashRefs, string Error = "");

public static class ModelRepack
{
    public static string PlayerPrefix(int playerId) => $"p{playerId & 0xFF:x2}";

    public static bool IsZipArchive(string path)
    {
        try
        {
            Span<byte> magic = stackalloc byte[2];
            using var file = File.OpenRead(path);
            return file.Read(magic) == 2 && magic[0] == (byte)'P' && magic[1] == (byte)'K';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsReadableArchive(string path) =>
        IsZipArchive(path) || MpqReader.LooksLikeMpq(path);

    internal static IEnumerable<(string Path, Func<byte[]> Load)> ReadArchiveEntries(string input)
    {
        if (IsZipArchive(input))
        {
            using var zip = ZipFile.OpenRead(input);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.Length == 0)
                {
                    continue;
                }

                yield return (entry.FullName, () =>
                {
                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                });
            }

            yield break;
        }

        if (MpqReader.TryReadEntries(input) is { } mpq)
        {
            foreach (var (path, data) in mpq)
            {
                yield return (path, () => data);
            }
        }
    }

    private const string CanonicalDir = "objects/";
    private static readonly string[] LinkDirs = ["objects/object_link", "objects/ot_obj_link"];

    public static string? NamespacedPath(string path, string prefix)
    {
        var normalized = path.Replace('\\', '/');
        var canonical = normalized.StartsWith("alt/", StringComparison.Ordinal)
            ? normalized["alt/".Length..]
            : normalized;
        foreach (var dir in LinkDirs)
        {
            if (canonical.StartsWith(dir, StringComparison.Ordinal))
            {
                return prefix + "objs/" + canonical[CanonicalDir.Length..];
            }
        }

        return null;
    }

    private sealed class RewritePlan
    {
        public List<(byte[] From, byte[] To)> Tokens { get; } = [];
        public Dictionary<ulong, byte[]> Hashes { get; } = [];

        public void AddToken(string from, string to)
        {
            if (from.Length != to.Length)
            {
                throw new ArgumentException("rewrite tokens must be the same length");
            }

            Tokens.Add((Encoding.ASCII.GetBytes(from), Encoding.ASCII.GetBytes(to)));
        }

        public void AddTarget(string canonical, string target)
        {
            AddPath(canonical, target);
            AddPath("alt/" + canonical, target);
        }

        private void AddPath(string oldPath, string newPath)
        {
            var oldHash = Crc64.OfPath(oldPath);
            var newHash = Crc64.OfPath(newPath);
            Add(WordSplit(oldHash), WordSplit(newHash));
            Add(BitConverter.GetBytes(oldHash), BitConverter.GetBytes(newHash));
            Add(BigEndian(oldHash), BigEndian(newHash));
        }

        private void Add(byte[] from, byte[] to) => Hashes[BitConverter.ToUInt64(from)] = to;

        private static byte[] WordSplit(ulong hash)
        {
            var bytes = new byte[8];
            BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), (uint)(hash >> 32));
            BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), (uint)hash);
            return bytes;
        }

        private static byte[] BigEndian(ulong hash)
        {
            var bytes = BitConverter.GetBytes(hash);
            Array.Reverse(bytes);
            return bytes;
        }
    }

    private static void ApplyRewrites(byte[] data, RewritePlan plan, ref int stringRefs, ref int hashRefs)
    {
        var span = data.AsSpan();
        for (var i = 0; i < data.Length;)
        {
            var remaining = data.Length - i;
            var rewrote = false;
            foreach (var (from, to) in plan.Tokens)
            {
                if (data[i] == from[0] && remaining >= from.Length
                    && span.Slice(i, from.Length).SequenceEqual(from))
                {
                    to.CopyTo(span[i..]);
                    stringRefs++;
                    i += from.Length;
                    rewrote = true;
                    break;
                }
            }

            if (rewrote)
            {
                continue;
            }

            if (remaining >= 8 && plan.Hashes.TryGetValue(BitConverter.ToUInt64(span.Slice(i, 8)), out var hash))
            {
                hash.CopyTo(span[i..]);
                hashRefs++;
                i += 8;
                continue;
            }

            i++;
        }
    }

    private static void AddAltAliases(Dictionary<string, byte[]> entries, string targetPrefix)
    {
        var marker = Encoding.ASCII.GetBytes("alt/" + targetPrefix);
        var aliases = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var data in entries.Values)
        {
            for (var at = 0; at < data.Length;)
            {
                var found = data.AsSpan(at).IndexOf(marker);
                if (found < 0)
                {
                    break;
                }

                var start = at + found + "alt/".Length;
                var end = start;
                while (end < data.Length && IsPathByte(data[end]))
                {
                    end++;
                }

                var reference = Encoding.ASCII.GetString(data, start, end - start);
                for (var length = reference.Length; length > targetPrefix.Length; length--)
                {
                    if (entries.TryGetValue(reference[..length], out var target))
                    {
                        aliases["alt/" + reference[..length]] = target;
                    }
                }

                at = end;
            }
        }

        foreach (var (path, data) in aliases)
        {
            entries[path] = data;
        }
    }

    private static bool IsPathByte(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'
            or (byte)'_' or (byte)'/' or (byte)'.' or (byte)'-' or (byte)' ';

    public static RepackResult Repack(IReadOnlyList<string> inputs, string output, int playerId) =>
        Build(inputs, output, playerId, linkObjectsOnly: false);

    public static RepackResult RepackVanillaFill(IReadOnlyList<string> baseArchives, string output, int playerId) =>
        Build(baseArchives, output, playerId, linkObjectsOnly: true);

    public static RepackResult PortDirectory(
        IReadOnlyList<string> inputs, string output, string fromDir, string toDir)
    {
        if (fromDir.Length != toDir.Length)
        {
            return new RepackResult(false, 0, 0, 0, "directory names must be the same length");
        }

        var plainPrefix = $"objects/{fromDir}/";
        var altPrefix = $"alt/objects/{fromDir}/";
        var plan = new RewritePlan();
        plan.AddToken(fromDir, toDir);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fromAlt = new Dictionary<string, bool>(StringComparer.Ordinal);

        try
        {
            foreach (var input in inputs)
            {
                foreach (var (rawPath, load) in ReadArchiveEntries(input))
                {
                    var path = rawPath.Replace('\\', '/');
                    var isAlt = path.StartsWith(altPrefix, StringComparison.Ordinal);
                    if (!isAlt && !path.StartsWith(plainPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var canonicalSource = isAlt ? path["alt/".Length..] : path;
                    var target = canonicalSource.Replace(fromDir, toDir);
                    if (!isAlt && fromAlt.GetValueOrDefault(target))
                    {
                        continue;
                    }

                    entries[target] = load();
                    fromAlt[target] = isAlt;
                    plan.AddTarget(canonicalSource, target);
                }
            }

            if (entries.Count == 0)
            {
                File.Delete(output);
                return new RepackResult(true, 0, 0, 0);
            }

            var stringRefs = 0;
            var hashRefs = 0;
            foreach (var data in entries.Values)
            {
                ApplyRewrites(data, plan, ref stringRefs, ref hashRefs);
            }

            AddAltAliases(entries, $"objects/{toDir}/");

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            var temporary = output + ".tmp";
            using (var file = File.Create(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var (path, data) in entries)
                {
                    using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                    stream.Write(data);
                }
            }

            File.Move(temporary, output, overwrite: true);
            return new RepackResult(true, entries.Count, stringRefs, hashRefs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new RepackResult(false, 0, 0, 0, ex.Message);
        }
    }

    private static RepackResult Build(
        IReadOnlyList<string> inputs, string output, int playerId, bool linkObjectsOnly)
    {
        var prefix = PlayerPrefix(playerId);
        var plan = new RewritePlan();
        foreach (var dir in LinkDirs)
        {
            plan.AddToken(dir, prefix + "objs/" + dir[CanonicalDir.Length..]);
        }

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fromAlt = new Dictionary<string, bool>(StringComparer.Ordinal);

        try
        {
            foreach (var input in inputs)
            {
                foreach (var (rawPath, load) in ReadArchiveEntries(input))
                {
                    var path = rawPath.Replace('\\', '/');
                    if (linkObjectsOnly && !LinkDirs.Any(d => path.StartsWith(d, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (NamespacedPath(path, prefix) is not { } target)
                    {
                        continue;
                    }

                    var isAlt = path.StartsWith("alt/", StringComparison.Ordinal);
                    if (!isAlt && fromAlt.GetValueOrDefault(target))
                    {
                        continue;
                    }

                    entries[target] = load();
                    fromAlt[target] = isAlt;
                    plan.AddTarget(isAlt ? path["alt/".Length..] : path, target);
                }
            }

            if (entries.Count == 0)
            {
                return new RepackResult(false, 0, 0, 0, "archive contains no player model entries");
            }

            var stringRefs = 0;
            var hashRefs = 0;
            foreach (var data in entries.Values)
            {
                ApplyRewrites(data, plan, ref stringRefs, ref hashRefs);
            }

            AddAltAliases(entries, prefix + "objs/");

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            var temporary = output + ".tmp";
            using (var file = File.Create(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var (path, data) in entries)
                {
                    using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                    stream.Write(data);
                }
            }

            File.Move(temporary, output, overwrite: true);
            return new RepackResult(true, entries.Count, stringRefs, hashRefs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new RepackResult(false, 0, 0, 0, ex.Message);
        }
    }
}
