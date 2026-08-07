using System.Buffers.Binary;
using System.IO.Compression;

namespace OotMM.Core.Models;

/// <summary>
/// Reader for the MPQ subset the old OTR exporters produce: header at offset zero, a
/// (listfile), zlib-only compression, no encryption or implosion. Anything outside that
/// subset reads as an unreadable archive rather than a partial one.
/// </summary>
public static class MpqReader
{
    private const uint FlagExists = 0x80000000;
    private const uint FlagImploded = 0x100;
    private const uint FlagCompressed = 0x200;
    private const uint FlagEncrypted = 0x10000;
    private const uint FlagSingleUnit = 0x1000000;
    private const byte ZlibMask = 0x02;

    private static readonly uint[] Crypt = BuildCryptTable();

    public static bool LooksLikeMpq(string path)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using var file = File.OpenRead(path);
            return file.Read(magic) == 4
                   && magic[0] == (byte)'M' && magic[1] == (byte)'P' && magic[2] == (byte)'Q'
                   && magic[3] == 0x1A;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static List<(string Path, byte[] Data)>? TryReadEntries(string path)
    {
        try
        {
            return ReadEntries(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static List<(string Path, byte[] Data)>? ReadEntries(byte[] archive)
    {
        try
        {
            if (archive.Length < 32 || archive[0] != 'M' || archive[1] != 'P' || archive[2] != 'Q'
                || archive[3] != 0x1A)
            {
                return null;
            }

            var sectorSize = 512 << BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(14));
            var hashPos = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(16));
            var blockPos = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(20));
            var hashCount = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(24));
            var blockCount = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(28));
            // The hash lookup masks with count-1, which only addresses a power-of-two table.
            if (hashCount <= 0 || (hashCount & (hashCount - 1)) != 0 || blockCount <= 0
                || sectorSize <= 0)
            {
                return null;
            }

            var hashTable = DecryptTable(archive, hashPos, hashCount, "(hash table)");
            var blockTable = DecryptTable(archive, blockPos, blockCount, "(block table)");
            if (hashTable is null || blockTable is null)
            {
                return null;
            }

            var listIndex = FindBlock(hashTable, hashCount, "(listfile)");
            if (listIndex < 0 || listIndex >= blockCount)
            {
                return null;
            }

            var listing = ExtractBlock(archive, sectorSize, blockTable, listIndex);
            if (listing is null)
            {
                return null;
            }

            var entries = new List<(string, byte[])>();
            foreach (var rawName in System.Text.Encoding.ASCII.GetString(listing)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = rawName.TrimEnd('\r');
                if (name.Length == 0 || name.StartsWith('('))
                {
                    continue;
                }

                var block = FindBlock(hashTable, hashCount, name);
                if (block < 0 || block >= blockCount)
                {
                    continue;
                }

                if (ExtractBlock(archive, sectorSize, blockTable, block) is not { } data)
                {
                    return null;
                }

                if (data.Length > 0)
                {
                    entries.Add((name, data));
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException
                                       or IndexOutOfRangeException or ArgumentOutOfRangeException
                                       or OverflowException)
        {
            return null;
        }
    }

    private static uint[]? DecryptTable(byte[] archive, int position, int count, string key)
    {
        var byteCount = count * 16;
        if (position < 0 || byteCount < 0 || position > archive.Length - byteCount)
        {
            return null;
        }

        var table = new uint[count * 4];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(position + i * 4));
        }

        Decrypt(table, HashString(key, 3));
        return table;
    }

    private static int FindBlock(uint[] hashTable, int hashCount, string name)
    {
        var hashA = HashString(name, 1);
        var hashB = HashString(name, 2);
        var index = (int)(HashString(name, 0) & (uint)(hashCount - 1));
        for (var probes = 0; probes < hashCount; probes++)
        {
            var entry = hashTable.AsSpan(index * 4, 4);
            if (entry[3] == 0xFFFFFFFF)
            {
                return -1;
            }

            if (entry[0] == hashA && entry[1] == hashB)
            {
                return (int)entry[3];
            }

            index = (index + 1) & (hashCount - 1);
        }

        return -1;
    }

    private static byte[]? ExtractBlock(byte[] archive, int sectorSize, uint[] blockTable, int block)
    {
        var filePos = (int)blockTable[block * 4];
        var compressedSize = (int)blockTable[block * 4 + 1];
        var size = (int)blockTable[block * 4 + 2];
        var flags = blockTable[block * 4 + 3];
        if ((flags & FlagExists) == 0 || (flags & (FlagEncrypted | FlagImploded)) != 0
            || filePos < 0 || size < 0 || compressedSize < 0
            || filePos > archive.Length - compressedSize)
        {
            return null;
        }

        if ((flags & FlagCompressed) == 0)
        {
            return archive.AsSpan(filePos, size).ToArray();
        }

        if ((flags & FlagSingleUnit) != 0)
        {
            return Inflate(archive.AsSpan(filePos, compressedSize), size);
        }

        var sectors = (size + sectorSize - 1) / sectorSize;
        var offsets = new int[sectors + 1];
        for (var i = 0; i <= sectors; i++)
        {
            offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(filePos + i * 4));
        }

        var result = new byte[size];
        var written = 0;
        for (var i = 0; i < sectors; i++)
        {
            var raw = archive.AsSpan(filePos + offsets[i], offsets[i + 1] - offsets[i]);
            var expected = Math.Min(sectorSize, size - written);
            if (raw.Length == expected)
            {
                raw.CopyTo(result.AsSpan(written)); // incompressible sector stored as-is
            }
            else if (Inflate(raw, expected) is { } sector)
            {
                sector.CopyTo(result.AsSpan(written));
            }
            else
            {
                return null;
            }

            written += expected;
        }

        return result;
    }

    private static byte[]? Inflate(ReadOnlySpan<byte> raw, int expectedSize)
    {
        if (raw.Length < 2 || raw[0] != ZlibMask)
        {
            return null;
        }

        using var stream = new ZLibStream(
            new MemoryStream(raw[1..].ToArray()), CompressionMode.Decompress);
        var result = new byte[expectedSize];
        var read = 0;
        while (read < expectedSize)
        {
            var got = stream.Read(result, read, expectedSize - read);
            if (got <= 0)
            {
                return null;
            }

            read += got;
        }

        return result;
    }

    // StormLib hashes the name bytes as-is (uppercased); slashes are not normalized.
    private static uint HashString(string name, int type)
    {
        var seed1 = 0x7FED7FEDu;
        var seed2 = 0xEEEEEEEEu;
        foreach (var c in name)
        {
            var ch = (uint)char.ToUpperInvariant(c);
            var value = Crypt[type * 0x100 + ch];
            seed1 = value ^ (seed1 + seed2);
            seed2 = ch + seed1 + seed2 + (seed2 << 5) + 3;
        }

        return seed1;
    }

    private static void Decrypt(uint[] data, uint key)
    {
        var seed = 0xEEEEEEEEu;
        for (var i = 0; i < data.Length; i++)
        {
            seed += Crypt[0x400 + (key & 0xFF)];
            var value = data[i] ^ (key + seed);
            data[i] = value;
            key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
            seed = value + seed + (seed << 5) + 3;
        }
    }

    private static uint[] BuildCryptTable()
    {
        var table = new uint[0x500];
        var seed = 0x00100001u;
        for (var index = 0; index < 0x100; index++)
        {
            for (var i = 0; i < 5; i++)
            {
                seed = (seed * 125 + 3) % 0x2AAAAB;
                var high = (seed & 0xFFFF) << 16;
                seed = (seed * 125 + 3) % 0x2AAAAB;
                table[index + i * 0x100] = high | (seed & 0xFFFF);
            }
        }

        return table;
    }
}
