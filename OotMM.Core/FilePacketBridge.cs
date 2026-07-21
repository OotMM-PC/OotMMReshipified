using System.Globalization;
using System.Text;

namespace OotMM.Core;

/// <summary>PacketEndOffsets parallels Packets: the byte offset just past each
/// packet's newline, so consumers can advance one durable packet at a time.</summary>
public sealed record FilePacketReadResult(
    IReadOnlyList<string> Packets,
    IReadOnlyList<long> PacketEndOffsets,
    long NextOffset,
    bool Success);

/// <summary>JSONL file transport shared with the C++ games; framing, torn-tail
/// and cursor rules must match FilePacketBridge.cpp exactly.</summary>
public static class FilePacketBridge
{
    /// <summary>Appends packets (sole writer per file); returns count written,
    /// 0 on any failure with the file rolled back to its original length.</summary>
    public static int AppendPacketLines(string path, IReadOnlyList<string> packets)
    {
        if (string.IsNullOrEmpty(path) || packets.Count == 0)
        {
            return 0;
        }

        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!RepairIncompleteAppendTail(path))
            {
                return 0;
            }

            var originalSize = File.Exists(path) ? new FileInfo(path).Length : 0;
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                var written = 0;
                foreach (var packet in packets)
                {
                    if (packet.Length == 0)
                    {
                        continue;
                    }

                    stream.Write(Encoding.UTF8.GetBytes(packet));
                    stream.WriteByte((byte)'\n');
                    written++;
                }

                stream.Flush(flushToDisk: true);
                return written;
            }
            catch (IOException)
            {
                RollBackFailedAppend(path, originalSize);
                return 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Reads whole packets from a byte offset; an unterminated tail
    /// stays unread with NextOffset at its first byte.</summary>
    public static FilePacketReadResult ReadPacketLines(string path, long offset)
    {
        var failed = new FilePacketReadResult([], [], offset, false);
        if (string.IsNullOrEmpty(path))
        {
            return failed;
        }

        try
        {
            if (!File.Exists(path))
            {
                return new FilePacketReadResult([], [], 0, true);
            }

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var snapshotSize = stream.Length;
            if (offset > snapshotSize)
            {
                offset = 0;
            }

            stream.Position = offset;
            var wanted = (int)(snapshotSize - offset);
            var bytes = new byte[wanted];
            var read = 0;
            while (read < wanted)
            {
                var chunk = stream.Read(bytes, read, wanted - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n', read - 1 < 0 ? 0 : read - 1);
            if (read == 0 || lastNewline < 0)
            {
                return new FilePacketReadResult([], [], offset, true);
            }

            // Split on bytes, not decoded text: offsets are byte offsets.
            var packets = new List<string>();
            var endOffsets = new List<long>();
            var cursor = 0;
            while (cursor <= lastNewline)
            {
                var newline = Array.IndexOf(bytes, (byte)'\n', cursor);
                if (newline < 0 || newline > lastNewline)
                {
                    break;
                }

                var length = newline - cursor;
                if (length > 0 && bytes[newline - 1] == (byte)'\r')
                {
                    length--;
                }

                if (length > 0)
                {
                    packets.Add(Encoding.UTF8.GetString(bytes, cursor, length));
                    endOffsets.Add(offset + newline + 1);
                }

                cursor = newline + 1;
            }

            return new FilePacketReadResult(packets, endOffsets, offset + lastNewline + 1, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return failed;
        }
    }

    /// <summary>Null means rewind to 0: cursor missing, malformed, or pointing
    /// past the packet file.</summary>
    public static long? ReadPacketCursor(string cursorPath, string packetPath)
    {
        if (string.IsNullOrEmpty(cursorPath) || string.IsNullOrEmpty(packetPath))
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(cursorPath, Encoding.UTF8);
            var text = reader.ReadLine() ?? "";
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
            {
                return null;
            }

            var packetSize = new FileInfo(packetPath).Length;
            return offset > packetSize ? null : offset;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Persists a read cursor atomically (tmp write, fsync, replace).</summary>
    public static bool WritePacketCursor(string cursorPath, long offset)
    {
        if (string.IsNullOrEmpty(cursorPath))
        {
            return false;
        }

        var temporary = cursorPath + ".tmp";
        try
        {
            var parent = Path.GetDirectoryName(cursorPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                stream.Write(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, cursorPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }

            return false;
        }
    }

    private static bool RepairIncompleteAppendTail(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        var size = stream.Length;
        if (size == 0)
        {
            return true;
        }

        stream.Position = size - 1;
        if (stream.ReadByte() == '\n')
        {
            return true;
        }

        const int chunkSize = 4096;
        var end = size;
        var chunk = new byte[chunkSize];
        while (end > 0)
        {
            var begin = end > chunkSize ? end - chunkSize : 0;
            var length = (int)(end - begin);
            stream.Position = begin;
            stream.ReadExactly(chunk, 0, length);
            var newline = Array.LastIndexOf(chunk, (byte)'\n', length - 1);
            if (newline >= 0)
            {
                stream.SetLength(begin + newline + 1);
                stream.Flush(flushToDisk: true);
                return true;
            }

            end = begin;
        }

        stream.SetLength(0);
        stream.Flush(flushToDisk: true);
        return true;
    }

    private static void RollBackFailedAppend(string path, long originalSize)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            stream.SetLength(originalSize);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException)
        {
        }
    }
}
