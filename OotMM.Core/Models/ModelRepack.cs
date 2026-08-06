using System.IO.Compression;
using System.Text;

namespace OotMM.Core.Models;

public sealed record RepackResult(bool Ok, int Kept, int StringRefs, int HashRefs, string Error = "");

/// <summary>
/// Rewrites a player model archive into a per-player asset namespace so a remote puppet can wear
/// it without reskinning the local player. Entries move from <c>objects/</c> to <c>pNNobjs/</c>,
/// which is the same length on purpose: references embedded in binary resources are patched in
/// place, as either the path string or its CRC64.
/// </summary>
public static class ModelRepack
{
    /// <summary>The namespace a player's assets live under. Three characters, so the directory
    /// prefix below is exactly as long as the one it replaces.</summary>
    public static string PlayerPrefix(int playerId) => $"p{playerId & 0xFF:x2}";

    private const string CanonicalDir = "objects/";
    private const string LinkPrefix = "objects/object_link";
    private const string AltLinkPrefix = "alt/objects/object_link";

    /// <summary>Where an entry lands in the player namespace, or null when it belongs to no
    /// namespace and must be dropped: keeping it would override the asset globally. Alt-asset and
    /// canonical entries unify, because alt sets reference each other by canonical path.</summary>
    public static string? NamespacedPath(string path, string prefix)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith(AltLinkPrefix, StringComparison.Ordinal))
        {
            return prefix + "objs/" + normalized["alt/objects/".Length..];
        }

        if (normalized.StartsWith(LinkPrefix, StringComparison.Ordinal))
        {
            return prefix + "objs/" + normalized[CanonicalDir.Length..];
        }

        return null;
    }

    private sealed class RewritePlan
    {
        // Same-length string substitutions applied wherever they appear in resource data.
        public List<(byte[] From, byte[] To)> Tokens { get; } = [];
        public Dictionary<byte, List<(byte[] From, byte[] To)>> HashBuckets { get; } = [];

        public void AddToken(string from, string to)
        {
            if (from.Length != to.Length)
            {
                throw new ArgumentException("rewrite tokens must be the same length");
            }

            Tokens.Add((Encoding.ASCII.GetBytes(from), Encoding.ASCII.GetBytes(to)));
        }

        public void AddPath(string oldPath, string newPath)
        {
            var oldHash = Crc64.OfPath(oldPath);
            var newHash = Crc64.OfPath(newPath);
            // Binary display lists store a reference as two little-endian words, high first;
            // other resources use a plain 64-bit value in either byte order.
            Add(WordSplit(oldHash), WordSplit(newHash));
            Add(BitConverter.GetBytes(oldHash), BitConverter.GetBytes(newHash));
            Add(BigEndian(oldHash), BigEndian(newHash));
        }

        private void Add(byte[] from, byte[] to)
        {
            if (!HashBuckets.TryGetValue(from[0], out var bucket))
            {
                bucket = [];
                HashBuckets[from[0]] = bucket;
            }

            bucket.Add((from, to));
        }

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

            if (remaining >= 8 && plan.HashBuckets.TryGetValue(data[i], out var bucket))
            {
                var matched = false;
                foreach (var (from, to) in bucket)
                {
                    if (span.Slice(i, 8).SequenceEqual(from))
                    {
                        to.CopyTo(span[i..]);
                        hashRefs++;
                        i += 8;
                        matched = true;
                        break;
                    }
                }

                if (matched)
                {
                    continue;
                }
            }

            i++;
        }
    }

    /// <summary>Repacks a model set into one namespaced archive. All inputs are read first so
    /// references between archives in the set (a skeleton pointing at a texture pack) resolve.
    /// </summary>
    public static RepackResult Repack(IReadOnlyList<string> inputs, string output, int playerId) =>
        Build(inputs, output, playerId, linkObjectsOnly: false);

    /// <summary>A namespace filled from the base game, so an asset a player's model does not
    /// override resolves to vanilla instead of falling through to the local player's own
    /// alternate assets.</summary>
    public static RepackResult RepackVanillaFill(IReadOnlyList<string> baseArchives, string output, int playerId) =>
        Build(baseArchives, output, playerId, linkObjectsOnly: true);

    /// <summary>Ports one asset directory to a same-length name (e.g. OoT's object_link_boy to
    /// MM's ot_obj_link_boy), rewriting entry paths, path strings, and CRC64 references alike.
    /// Zero kept entries is success without output: the set simply has nothing to port.</summary>
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
                using var zip = ZipFile.OpenRead(input);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.Length == 0)
                    {
                        continue;
                    }

                    var path = entry.FullName.Replace('\\', '/');
                    var isAlt = path.StartsWith(altPrefix, StringComparison.Ordinal);
                    if (!isAlt && !path.StartsWith(plainPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // alt/ is a load-time overlay the target game may have disabled; entries
                    // flatten to canonical so the port always resolves. Alt (HD) wins collisions.
                    var canonicalSource = isAlt ? path["alt/".Length..] : path;
                    var target = canonicalSource.Replace(fromDir, toDir);
                    if (!isAlt && fromAlt.GetValueOrDefault(target))
                    {
                        continue;
                    }

                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    entries[target] = buffer.ToArray();
                    fromAlt[target] = isAlt;
                    plan.AddPath(path, target);
                    if (isAlt)
                    {
                        plan.AddPath(canonicalSource, target);
                    }
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
        plan.AddToken(LinkPrefix, prefix + "objs/object_link");
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            foreach (var input in inputs)
            {
                using var zip = ZipFile.OpenRead(input);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.Length == 0)
                    {
                        continue;
                    }

                    var path = entry.FullName.Replace('\\', '/');
                    // Base archives hold the whole game; only the Link namespace is wanted.
                    if (linkObjectsOnly && !path.StartsWith(LinkPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (NamespacedPath(path, prefix) is not { } target)
                    {
                        continue;
                    }

                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    // A later archive in the set overrides an earlier one, matching mount order.
                    entries[target] = buffer.ToArray();
                    plan.AddPath(path, target);
                    if (path.StartsWith(AltLinkPrefix, StringComparison.Ordinal))
                    {
                        plan.AddPath(path["alt/".Length..], target);
                    }
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
