using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OotMM.Core.Models;

public static class VoicePort
{
    public const string OotFont = "audio/fonts/00_Sound_Effects_1";
    public const string MmFont = "audio/fonts/Soundfont_0";
    public const string AdultPrefix = "audio/samples/Adult Link - ";
    public const string ChildPrefix = "audio/samples/Child Link - ";
    public const string SplitSamplePrefix = "audio/samples/OotLinkVoiceSlot";

    private const int ResourceHeader = 64;

    private readonly record struct Slot(string Sample, int Start, int End);

    /// <summary>The sample each sound-effect slot of a binary sound font plays; empty for an unused slot.</summary>
    public static IReadOnlyList<string> SoundEffectSamples(byte[] font) =>
        ReadSlots(font).Select(slot => slot.Sample).ToList();

    private static IReadOnlyList<Slot> ReadSlots(byte[] font)
    {
        try
        {
            var reader = new FontReader(font, ResourceHeader);
            reader.Skip(4 + 1 + 1 + 2 + 2 + 2);
            var drums = reader.Count();
            var instruments = reader.Count();
            var effects = reader.Count();
            for (var i = 0; i < drums; i++)
            {
                reader.Skip(3);
                reader.Skip(reader.Count() * 4);
                reader.Skip(1);
                reader.Text();
                reader.Skip(4);
            }

            for (var i = 0; i < instruments; i++)
            {
                reader.Skip(5);
                reader.Skip(reader.Count() * 4);
                for (var range = 0; range < 3; range++)
                {
                    if (reader.Flag())
                    {
                        reader.Skip(1);
                        reader.Text();
                        reader.Skip(4);
                    }
                }
            }

            var slots = new Slot[effects];
            for (var i = 0; i < effects; i++)
            {
                slots[i] = new Slot("", 0, 0);
                if (reader.Flag())
                {
                    reader.Skip(1);
                    var start = reader.Position;
                    var sample = reader.Text();
                    slots[i] = new Slot(sample, start, reader.Position);
                    reader.Skip(4);
                }
            }

            return slots;
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    /// <summary>
    /// Maps OoT voice samples to the MM samples in the same sound-effect slots. Both games keep Link's
    /// voice in matching slots, so the lowest slot decides when MM reuses one sample for several.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MapSlots(byte[] ootFont, byte[] mmFont, string ootPrefix)
    {
        var oot = SoundEffectSamples(ootFont);
        var mm = SoundEffectSamples(mmFont);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var slot = 0; slot < Math.Min(oot.Count, mm.Count); slot++)
        {
            if (!oot[slot].StartsWith(ootPrefix, StringComparison.Ordinal) || mm[slot].Length == 0
                || map.ContainsKey(oot[slot]) || !taken.Add(mm[slot]))
            {
                continue;
            }

            map[oot[slot]] = mm[slot];
        }

        return map;
    }

    /// <summary>
    /// MM points several of Link's voice slots at one stand-in sample. Returns MM's font with each such slot
    /// naming a sample of its own, and the OoT sample that belongs under each new name.
    /// </summary>
    public static (byte[] Font, IReadOnlyDictionary<string, string> Samples) SplitSharedSlots(
        byte[] ootFont, byte[] mmFont, string ootPrefix)
    {
        var oot = SoundEffectSamples(ootFont);
        var mm = ReadSlots(mmFont);
        var direct = MapSlots(ootFont, mmFont, ootPrefix);
        var samples = new Dictionary<string, string>(StringComparer.Ordinal);
        var renamed = new SortedDictionary<int, string>();
        for (var slot = 0; slot < Math.Min(oot.Count, mm.Count); slot++)
        {
            if (!oot[slot].StartsWith(ootPrefix, StringComparison.Ordinal) || mm[slot].Sample.Length == 0
                || direct.ContainsKey(oot[slot]))
            {
                continue;
            }

            if (!samples.TryGetValue(oot[slot], out var name))
            {
                name = $"{SplitSamplePrefix}{slot}_META";
                samples[oot[slot]] = name;
            }

            renamed[slot] = name;
        }

        if (renamed.Count == 0)
        {
            return (mmFont, samples);
        }

        using var output = new MemoryStream();
        var copied = 0;
        Span<byte> length = stackalloc byte[4];
        foreach (var (slot, name) in renamed)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            output.Write(mmFont, copied, mm[slot].Start - copied);
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
            output.Write(length);
            output.Write(bytes);
            copied = mm[slot].End;
        }

        output.Write(mmFont, copied, mmFont.Length - copied);
        return (output.ToArray(), samples);
    }

    /// <summary>Writes or replaces one entry of the port archive.</summary>
    public static void SetEntry(string portArchive, string path, byte[] data)
    {
        using var zip = ZipFile.Open(portArchive, ZipArchiveMode.Update);
        zip.GetEntry(path)?.Delete();
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        stream.Write(data);
    }

    /// <summary>Copies the mapped voice samples found in the source archives into the port archive under MM's names.</summary>
    public static int AddSamples(
        IReadOnlyList<string> sources, string portArchive, IReadOnlyDictionary<string, string> map)
    {
        var samples = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var (rawPath, load) in ModelRepack.ReadArchiveEntries(source))
            {
                if (map.TryGetValue(rawPath.Replace('\\', '/'), out var target))
                {
                    samples[target] = load();
                }
            }
        }

        if (samples.Count == 0)
        {
            return 0;
        }

        using var zip = ZipFile.Open(portArchive, ZipArchiveMode.Update);
        foreach (var (path, data) in samples.OrderBy(sample => sample.Key, StringComparer.Ordinal))
        {
            zip.GetEntry(path)?.Delete();
            using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
            stream.Write(data);
        }

        return samples.Count;
    }

    /// <summary>Reads one entry from the first base archive that has it.</summary>
    public static byte[]? ReadBaseEntry(IEnumerable<string> archives, string entryPath)
    {
        foreach (var archive in archives)
        {
            if (!File.Exists(archive) || !ModelRepack.IsZipArchive(archive))
            {
                continue;
            }

            using var zip = ZipFile.OpenRead(archive);
            if (zip.GetEntry(entryPath) is not { } entry)
            {
                continue;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        return null;
    }

    private ref struct FontReader(byte[] data, int position)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position = position;

        public readonly int Position => _position;

        public void Skip(int count)
        {
            if (count < 0 || _position > _data.Length - count)
            {
                throw new InvalidDataException("sound font ends early");
            }

            _position += count;
        }

        public int Count()
        {
            var start = _position;
            Skip(4);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[start..]);
            if (value > (uint)_data.Length)
            {
                throw new InvalidDataException("sound font count is out of range");
            }

            return (int)value;
        }

        public bool Flag()
        {
            var start = _position;
            Skip(1);
            return _data[start] != 0;
        }

        public string Text()
        {
            var length = Count();
            var start = _position;
            Skip(length);
            return Encoding.UTF8.GetString(_data.Slice(start, length));
        }
    }
}
