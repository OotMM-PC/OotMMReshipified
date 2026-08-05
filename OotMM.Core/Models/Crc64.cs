namespace OotMM.Core.Models;

/// <summary>libultraship's StrHash64: CRC-64/ECMA-182, MSB-first, all-ones init, no final
/// inversion. Archives index entries by this, and binary resources reference each other by it,
/// so a repacked path and its embedded references must hash identically to the real thing.
/// </summary>
public static class Crc64
{
    private const ulong Polynomial = 0x42F0E1EBA9EA3693UL;
    private const ulong Initial = 0xFFFFFFFFFFFFFFFFUL;

    private static readonly ulong[] Table = BuildTable();

    private static ulong[] BuildTable()
    {
        var table = new ulong[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ulong)i << 56;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000000000000000UL) != 0 ? (crc << 1) ^ Polynomial : crc << 1;
            }

            table[i] = crc;
        }

        return table;
    }

    /// <summary>Hashes the bytes of an ASCII resource path, matching CRC64(const char*).</summary>
    public static ulong OfPath(string path)
    {
        var crc = Initial;
        foreach (var c in path)
        {
            crc = Table[(byte)(crc >> 56) ^ (byte)c] ^ (crc << 8);
        }

        return crc;
    }
}
