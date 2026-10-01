using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace OotMM.Launcher.Models;

public sealed record OotmmAssetBuildResult(bool Ok, int Entries, IReadOnlyList<string> Log, string Error);

/// <summary>Builds target-native OoTMM gameplay assets from each game's own archives.</summary>
public static class OotmmAssetBuilder
{
    public const string ArchiveName = "ootmm_assets.o2r";

    private const string OutputDirectory = "objects/ootmm_csmc";
    private const string LegacyOotArchive = "ootmm_mm_models.o2r";
    private const string LegacyMmArchive = "ootmm_oot_models.o2r";
    private static readonly ulong[] Crc64Table = CreateCrc64Table();

    private static readonly Category[] Categories =
    [
        new("BossKey", 0x00, 0x00, 0xff),
        new("Major", 0xff, 0xff, 0x00),
        new("Key", 0x44, 0x44, 0x44),
        new("Spider", 0xff, 0xff, 0xff),
        new("Fairy", 0xff, 0x7a, 0xfb),
        new("Heart", 0xff, 0x00, 0x00),
        new("Soul", 0x34, 0x0b, 0x9c),
        new("Map", 0xc7, 0x50, 0x00),
    ];

    private static readonly Dictionary<string, (uint Primitive, uint Environment)> SkulltulaColors = new()
    {
        ["Normal"] = (0x755726ff, 0x0c0904ff),
        ["BossKey"] = (0x0000ffff, 0x000019ff),
        ["Key"] = (0x646464ff, 0x0a0a0aff),
        ["Spider"] = (0xffffffff, 0x191919ff),
        ["Fairy"] = (0xe69df2ff, 0x170f18ff),
        ["Heart"] = (0xff0000ff, 0x190000ff),
        ["Soul"] = (0x340b9cff, 0x05010fff),
        ["Map"] = (0xfc5603ff, 0x4a1900ff),
    };

    private static readonly Dictionary<string, (string Side, string Top)> PotArt = new()
    {
        ["BossKey"] = ("pots/bosskey_side.png", "pots/bosskey_top.png"),
        ["Major"] = ("pots/major_side.png", "pots/major_top.png"),
        ["Key"] = ("pots/key_side.png", "pots/spider_top.png"),
        ["Spider"] = ("pots/spider_side.png", "pots/spider_top.png"),
        ["Fairy"] = ("pots/fairy_side.png", "pots/fairy_top.png"),
        ["Heart"] = ("pots/heart_side.png", "pots/heart_top.png"),
        ["Soul"] = ("pots/soul_side.png", "pots/soul_top.png"),
        ["Map"] = ("pots/map_side.png", "pots/spider_top.png"),
    };

    private static readonly Dictionary<string, (string Front, string Side)> ChestArt = new()
    {
        ["Major"] = ("chests/major_front.png", "chests/major_side.png"),
        ["Key"] = ("chests/key_front.png", "chests/key_side.png"),
        ["Spider"] = ("chests/spider_front.png", "chests/spider_side.png"),
        ["Fairy"] = ("chests/fairy_front.png", "chests/fairy_side.png"),
        ["Heart"] = ("chests/heart_front.png", "chests/heart_side.png"),
        ["Soul"] = ("chests/soul_front.png", "chests/soul_side.png"),
        ["Map"] = ("chests/map_front.png", "chests/map_side.png"),
    };

    public static bool NeedsBuild(string exePath, bool isOot)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(exePath)!;
        var inputs = InputArchives(dir, isOot);
        if (inputs.Any(path => !File.Exists(path)))
        {
            return false;
        }

        var mods = Path.Combine(dir, "mods");
        var legacy = Path.Combine(mods, isOot ? LegacyOotArchive : LegacyMmArchive);
        var output = Path.Combine(mods, ArchiveName);
        if (File.Exists(legacy) || File.Exists(legacy + ".disabled") || !File.Exists(output))
        {
            return true;
        }

        var outputStamp = File.GetLastWriteTimeUtc(output);
        var assembly = Path.Combine(AppContext.BaseDirectory, typeof(OotmmAssetBuilder).Assembly.GetName().Name + ".dll");
        var builderPath = File.Exists(assembly) ? assembly : Environment.ProcessPath ?? "";
        var builderStamp = builderPath.Length > 0 ? File.GetLastWriteTimeUtc(builderPath) : DateTime.MinValue;
        return outputStamp < builderStamp || inputs.Any(path => outputStamp < File.GetLastWriteTimeUtc(path));
    }

    public static OotmmAssetBuildResult Build(string ootExe, string mmExe)
    {
        var log = new List<string>();
        var entries = 0;
        try
        {
            var ootInputs = ExistingInputArchives(ootExe, isOot: true);
            var mmInputs = ExistingInputArchives(mmExe, isOot: false);
            if (File.Exists(ootExe))
            {
                entries += BuildGame(ootExe, isOot: true, mmInputs, "mm", log);
            }
            if (File.Exists(mmExe))
            {
                entries += BuildGame(mmExe, isOot: false, ootInputs, "ot", log);
            }
            RemoveLegacyArchive(ootExe, LegacyOotArchive);
            RemoveLegacyArchive(mmExe, LegacyMmArchive);
            return new OotmmAssetBuildResult(true, entries, log, "");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new OotmmAssetBuildResult(false, entries, log, ex.Message);
        }
    }

    private static int BuildGame(
        string exePath, bool isOot, IReadOnlyList<string> foreignInputs,
        string foreignNamespace, List<string> log)
    {
        var dir = Path.GetDirectoryName(exePath)
                  ?? throw new InvalidDataException("A game executable directory is required.");
        var inputs = InputArchives(dir, isOot);
        var missing = inputs.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            throw new InvalidDataException($"Cannot build OoTMM assets without {Path.GetFileName(missing)}.");
        }

        using var source = new ArchiveSet(inputs);
        Directory.CreateDirectory(Path.Combine(dir, "mods"));
        var output = Path.Combine(dir, "mods", ArchiveName);
        var temporary = output + ".tmp";
        File.Delete(temporary);

        int count;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var writer = new AssetWriter(zip);
                WriteCsmcAssets(writer, source, isOot, log);
                WriteRustyDoorLock(writer, source, log);
                WriteMenuAssets(writer, source, isOot, log);
                if (!isOot)
                {
                    WritePalmTree(writer, source, log);
                }
                WriteForeignObjects(writer, foreignInputs, foreignNamespace, log);
                count = writer.Count;
            }

            if (count == 0)
            {
                throw new InvalidDataException($"No OoTMM assets could be generated for {(isOot ? "OoT" : "MM")}.");
            }

            File.Move(temporary, output, overwrite: true);
            File.Delete(output + ".disabled");
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        log.Add($"{(isOot ? "OoT" : "MM")}: wrote {count} entries to {ArchiveName}.");
        return count;
    }

    private static string[] InputArchives(string dir, bool isOot) => isOot
        ? [Path.Combine(dir, "oot.o2r"), Path.Combine(dir, "soh.o2r")]
        : [Path.Combine(dir, "mm.o2r"), Path.Combine(dir, "2ship.o2r")];

    private static string[] ExistingInputArchives(string exePath, bool isOot)
    {
        var dir = Path.GetDirectoryName(exePath);
        return string.IsNullOrEmpty(dir)
            ? []
            : InputArchives(dir, isOot).Where(File.Exists).ToArray();
    }

    private static void RemoveLegacyArchive(string exePath, string name)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(dir))
        {
            File.Delete(Path.Combine(dir, "mods", name));
            File.Delete(Path.Combine(dir, "mods", name + ".disabled"));
        }
    }

    private static readonly (string Source, string Output)[] RustyDoorLock =
    [
        ("objects/gameplay_dangeon_keep/gDoorChainDL", "objects/ootmm_door_lock/gRustyDoorChainDL"),
        ("objects/gameplay_dangeon_keep/gDoorLockDL", "objects/ootmm_door_lock/gRustyDoorLockDL"),
    ];

    private static readonly Category RustyLockColor = new("Rusty", 255, 140, 64);

    private static void WriteRustyDoorLock(AssetWriter output, ArchiveSet source, List<string> log)
    {
        var baked = new List<(string Name, byte[] Data)>();
        foreach (var (path, name) in RustyDoorLock)
        {
            if (!source.TryRead(path, out var data))
            {
                log.Add($"Skipped the rusty door lock: {path} is missing.");
                return;
            }

            try
            {
                baked.Add((name, RecolorPrimitive(data, RustyLockColor, 0xffffffff)));
            }
            catch (InvalidDataException ex)
            {
                log.Add($"Skipped the rusty door lock: {path} {ex.Message}.");
                return;
            }
        }

        foreach (var (name, data) in baked)
        {
            output.Emit(name, data);
        }

        log.Add("Baked the rusty door lock.");
    }

    private static void WritePalmTree(AssetWriter output, ArchiveSet source, List<string> log)
    {
        const string palmTree = "objects/object_obj_yasi/gPalmTreeDL";
        if (!source.TryRead(palmTree, out var data))
        {
            log.Add($"Skipped the shuffled palm tree: {palmTree} is missing.");
            return;
        }

        var offset = 0x58;
        while (offset + 8 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var operation = word0 >> 24;
            if (operation == 0xdf)
            {
                break;
            }

            var size = IsExpandedOperation(word0) ? 16 : 8;
            if (operation == 0x32 && ((word0 >> 12) & 0xff) == 9 && ReadUInt32(data, offset + 4) == 0xf0
                && offset + size + 16 <= data.Length
                && ReadUInt32(data, offset + size) >> 24 == 0x06 && ReadUInt32(data, offset + size + 8) >> 24 == 0x05)
            {
                Array.Clear(data, offset, size + 16);
                output.Emit("objects/ootmm_yasi/gPalmTreeShuffledDL", data);
                log.Add("Baked the shuffled palm tree.");
                return;
            }

            offset += size;
        }

        log.Add("Skipped the shuffled palm tree: the deku nut geometry was not found.");
    }

    private static void WriteCsmcAssets(AssetWriter output, ArchiveSet source, bool isOot, List<string> log)
    {
        var byHash = source.Names.ToDictionary(PathCrc64, name => name);
        foreach (var family in Families(isOot))
        {
            if (family.Mode == FamilyMode.Chest)
            {
                WriteChestFamily(output, source, family, log);
                WriteSongTagAssets(output, source, family, log);
                continue;
            }

            if (!source.TryRead(family.DisplayList, out var displayList))
            {
                log.Add($"Skipped {family.Name}: {family.DisplayList} is missing.");
                continue;
            }

            var categories = family.Mode == FamilyMode.SkulltulaTint
                ? SkulltulaColors.Keys.Select(name => new Category(name, 0, 0, 0))
                : Categories.Concat(family.Extra);
            foreach (var category in categories)
            {
                WriteFamilyCategory(output, source, byHash, family, category, displayList, log);
            }
        }
    }

    private static void WriteForeignObjects(
        AssetWriter output, IReadOnlyList<string> inputs, string nameSpace, List<string> log)
    {
        if (inputs.Count == 0)
        {
            log.Add($"Skipped {nameSpace} item models because the source archives are unavailable.");
            return;
        }

        using var source = new ArchiveSet(inputs);
        var mapping = source.Names
            .Where(IsObjectResource)
            .ToDictionary(path => path, path => NamespaceObject(path, nameSpace), StringComparer.Ordinal);
        var replacements = HashReplacements(mapping);

        var animationData = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (oldPath, newPath) in mapping)
        {
            var data = RewriteKnownPaths(source.Read(oldPath), mapping);
            data = SwapKnownHashes(data, replacements);
            CollectLinkAnimationData(data, animationData);
            output.Emit(newPath, data);
        }

        log.Add($"Copied {mapping.Count} namespaced {nameSpace} item-model resources.");
        WriteLinkAnimationData(output, source, animationData, nameSpace, log);
        WriteForeignMusic(output, source, nameSpace, log);
    }

    // Kamaro's one instrument is byte-identical to OoT's Water Temple flute, so only the sequence travels.
    private const string KamaroDanceSource = "audio/sequences/KamaroDance_71";
    private const string KamaroDanceOutput = "custom/music/KamaroDance_fanfare";
    private const byte OotWaterTempleFont = 25;

    // Both games' ocarina fanfares use soundfont 0 instrument 53, so the sequences travel untouched.
    private static readonly (string Source, string Output)[] OcarinaFanfares =
    [
        ("audio/sequences/SongOfSoaring(Ocarina)_47", "custom/music/SongOfSoaring_fanfare"),
        ("audio/sequences/SongOfHealing(Ocarina)_48", "custom/music/SongOfHealing_fanfare"),
        ("audio/sequences/SongOfDoubleTime(Ocarina)_4A", "custom/music/SongOfDoubleTime_fanfare"),
        ("audio/sequences/LullabyIntro(Ocarina)_51", "custom/music/GoronLullabyIntro_fanfare"),
        ("audio/sequences/SonataOfAwakening(Ocarina)_5B", "custom/music/SonataOfAwakening_fanfare"),
        ("audio/sequences/GoronLullaby(Ocarina)_5C", "custom/music/GoronLullaby_fanfare"),
        ("audio/sequences/NewWaveBossaNova(Ocarina)_5D", "custom/music/NewWaveBossaNova_fanfare"),
        ("audio/sequences/ElegyOfEmptiness(Ocarina)_5E", "custom/music/ElegyOfEmptiness_fanfare"),
        ("audio/sequences/OathToOrder(Ocarina)_5F", "custom/music/OathToOrder_fanfare"),
    ];

    // The six warp-song jingles use OoT's warp-song font, which has no byte-identical MM
    // counterpart, so that font and its samples ship too and the game retargets the font index.
    private static readonly (string Source, string Output)[] OotOcarinaFanfares =
    [
        ("audio/sequences/052_Minuet_of_Forest", "custom/music/MinuetOfForest_fanfare"),
        ("audio/sequences/051_Bolero_of_Fire", "custom/music/BoleroOfFire_fanfare"),
        ("audio/sequences/053_Serenade_of_Water", "custom/music/SerenadeOfWater_fanfare"),
        ("audio/sequences/054_Requiem_of_Spirit", "custom/music/RequiemOfSpirit_fanfare"),
        ("audio/sequences/055_Nocturne_of_Shadow", "custom/music/NocturneOfShadow_fanfare"),
        ("audio/sequences/037_Prelude_of_Light", "custom/music/PreludeOfLight_fanfare"),
        ("audio/sequences/070_Zeldas_Lullaby", "custom/music/ZeldasLullaby_fanfare"),
        ("audio/sequences/068_Sarias_Song", "custom/music/SariasSong_fanfare"),
    ];

    private const string OotWarpFontSource = "audio/fonts/18_Warp_Songs";
    private const string OotWarpFontOutput = "custom/fonts/OotWarpSongs";
    private const string OotSamplePrefix = "audio/samples/";
    // Same length as the source prefix, so the font's length-prefixed strings rewrite in place.
    private const string OotSampleOutputPrefix = "custom/otsmpl/";

    private static void WriteOotOcarinaMusic(AssetWriter output, ArchiveSet source, List<string> log)
    {
        var fanfares = 0;
        foreach (var (sourcePath, outputPath) in OotOcarinaFanfares)
        {
            if (source.TryRead(sourcePath, out var fanfare))
            {
                output.Emit(outputPath, fanfare);
                fanfares++;
            }
            else
            {
                log.Add($"Skipped {sourcePath}: the oot archives do not carry that sequence.");
            }
        }

        if (fanfares > 0)
        {
            log.Add($"Copied {fanfares} OoT ocarina song fanfares.");
        }

        if (!source.TryRead(OotWarpFontSource, out var font))
        {
            log.Add($"Skipped {OotWarpFontSource}: the oot archives do not carry that font.");
            return;
        }

        var sampleNames = new SortedSet<string>(StringComparer.Ordinal);
        if (!TryRetargetFontSamplePaths(font, sampleNames))
        {
            log.Add($"Skipped {OotWarpFontSource}: its sample references are not the expected shape.");
            return;
        }

        foreach (var name in sampleNames)
        {
            if (!source.TryRead(OotSamplePrefix + name, out var sample))
            {
                log.Add($"Skipped {OotWarpFontSource}: sample {name} is missing.");
                return;
            }
            output.Emit(OotSampleOutputPrefix + name, sample);
        }

        output.Emit(OotWarpFontOutput, font);
        log.Add($"Copied the OoT warp-song font and {sampleNames.Count} samples.");
    }

    private static bool TryRetargetFontSamplePaths(byte[] font, ICollection<string> names)
    {
        var prefix = Encoding.ASCII.GetBytes(OotSamplePrefix);
        var replacement = Encoding.ASCII.GetBytes(OotSampleOutputPrefix);
        for (var i = 4; i + prefix.Length <= font.Length; i++)
        {
            if (!font.AsSpan(i, prefix.Length).SequenceEqual(prefix))
            {
                continue;
            }
            var length = BinaryPrimitives.ReadUInt32LittleEndian(font.AsSpan(i - 4));
            if (length <= (uint)prefix.Length || length > 128 || i + length > (uint)font.Length)
            {
                return false;
            }
            names.Add(Encoding.ASCII.GetString(font, i + prefix.Length, (int)length - prefix.Length));
            replacement.CopyTo(font, i);
            i += (int)length - 1;
        }
        return names.Count > 0;
    }

    private static void WriteForeignMusic(
        AssetWriter output, ArchiveSet source, string nameSpace, List<string> log)
    {
        if (nameSpace == "ot")
        {
            WriteOotOcarinaMusic(output, source, log);
            return;
        }
        if (nameSpace != "mm")
        {
            return;
        }

        if (!source.TryRead(KamaroDanceSource, out var sequence))
        {
            log.Add($"Skipped {KamaroDanceSource}: the mm archives do not carry that sequence.");
        }
        else if (!TryRetargetSequenceFont(sequence, OotWaterTempleFont))
        {
            log.Add($"Skipped {KamaroDanceSource}: its font list is not the expected shape.");
        }
        else
        {
            output.Emit(KamaroDanceOutput, sequence);
            log.Add("Copied Kamaro's dance sequence.");
        }

        var fanfares = 0;
        foreach (var (sourcePath, outputPath) in OcarinaFanfares)
        {
            if (source.TryRead(sourcePath, out var fanfare))
            {
                output.Emit(outputPath, fanfare);
                fanfares++;
            }
            else
            {
                log.Add($"Skipped {sourcePath}: the mm archives do not carry that sequence.");
            }
        }

        if (fanfares > 0)
        {
            log.Add($"Copied {fanfares} MM ocarina song fanfares.");
        }
    }

    private static bool TryRetargetSequenceFont(byte[] sequence, byte font)
    {
        const int header = 64;
        if (sequence.Length < header + sizeof(uint))
        {
            return false;
        }

        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(sequence.AsSpan(header));
        var fontCount = header + sizeof(uint) + (int)dataSize + 3;
        if (fontCount + sizeof(uint) >= sequence.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(sequence.AsSpan(fontCount)) != 1)
        {
            return false;
        }

        sequence[fontCount + sizeof(uint)] = font;
        return true;
    }

    // A link animation names its frame data inline under misc/, which no object folder covers;
    // RewriteKnownPaths only rewrites objects/ paths, so these names stay un-namespaced.
    private static void CollectLinkAnimationData(byte[] data, HashSet<string> referenced)
    {
        ReadOnlySpan<byte> folder = "misc/link_animetion/"u8;
        var source = (ReadOnlySpan<byte>)data;
        for (var offset = 0; offset + folder.Length <= source.Length; offset++)
        {
            if (!source[offset..].StartsWith(folder))
            {
                continue;
            }

            var end = offset;
            while (end < source.Length && source[end] is >= 0x20 and < 0x7F)
            {
                end++;
            }

            referenced.Add(Encoding.UTF8.GetString(source[offset..end]));
            offset = end - 1;
        }
    }

    private static void WriteLinkAnimationData(
        AssetWriter output, ArchiveSet source, HashSet<string> referenced, string nameSpace, List<string> log)
    {
        var copied = 0;
        foreach (var path in referenced)
        {
            if (source.TryRead(path, out var frames))
            {
                output.Emit(path, frames);
                copied++;
            }
            else
            {
                log.Add($"Skipped {path}: the {nameSpace} archives do not carry that animation data.");
            }
        }

        if (copied > 0)
        {
            log.Add($"Copied {copied} {nameSpace} link-animation data resources.");
        }
    }

    // OoT stores icons under textures/icon_item_*, MM at icon_item_*_yar with no textures/
    // prefix; some models live in an actor's overlay rather than an object.
    private static bool IsObjectResource(string path) =>
        path.StartsWith("objects/", StringComparison.Ordinal)
        || path.StartsWith("overlays/", StringComparison.Ordinal)
        || path.StartsWith("textures/icon_item", StringComparison.Ordinal)
        || path.StartsWith("icon_item", StringComparison.Ordinal)
        || path.StartsWith("textures/item_name", StringComparison.Ordinal)
        || path.StartsWith("item_name", StringComparison.Ordinal)
        || path.StartsWith("textures/map_name", StringComparison.Ordinal)
        || path.StartsWith("map_name", StringComparison.Ordinal)
        || path.StartsWith("do_action_static", StringComparison.Ordinal);

    private static string NamespaceObject(string path, string nameSpace)
    {
        const string prefix = "objects/object_";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return $"objects/{nameSpace}_obj_{path[prefix.Length..]}";
        }

        const string overlays = "overlays/";
        if (path.StartsWith(overlays, StringComparison.Ordinal))
        {
            return $"overlays/{nameSpace}_{path[overlays.Length..]}";
        }

        const string textures = "textures/";
        if (path.StartsWith(textures, StringComparison.Ordinal))
        {
            return $"textures/{nameSpace}_{path[textures.Length..]}";
        }

        if (path.StartsWith("icon_item", StringComparison.Ordinal)
            || path.StartsWith("item_name", StringComparison.Ordinal)
            || path.StartsWith("map_name", StringComparison.Ordinal)
            || path.StartsWith("do_action_static", StringComparison.Ordinal))
        {
            return $"{nameSpace}_{path}";
        }

        const string objects = "objects/";
        var separator = path.IndexOf('/', objects.Length);
        return separator < 0
            ? path
            : $"objects/{nameSpace}_{path[objects.Length..]}";
    }

    private static Dictionary<ulong, byte[]> HashReplacements(IReadOnlyDictionary<string, string> mapping)
    {
        var replacements = new Dictionary<ulong, byte[]>();
        foreach (var (oldPath, newPath) in mapping)
        {
            var oldEncodings = HashEncodings(PathCrc64(oldPath));
            var newEncodings = HashEncodings(PathCrc64(newPath));
            for (var i = 0; i < oldEncodings.Length; i++)
            {
                replacements[BinaryPrimitives.ReadUInt64LittleEndian(oldEncodings[i])] = newEncodings[i];
            }
        }
        return replacements;
    }

    private static byte[] SwapKnownHashes(byte[] source, IReadOnlyDictionary<ulong, byte[]> replacements)
    {
        var result = (byte[])source.Clone();
        for (var offset = 0; offset <= result.Length - sizeof(ulong); offset++)
        {
            var encoded = BinaryPrimitives.ReadUInt64LittleEndian(result.AsSpan(offset));
            if (!replacements.TryGetValue(encoded, out var replacement))
            {
                continue;
            }

            replacement.CopyTo(result, offset);
            offset += sizeof(ulong) - 1;
        }
        return result;
    }

    private static byte[] RewriteKnownPaths(
        byte[] source, IReadOnlyDictionary<string, string> mapping)
    {
        if (source.Length > 0 && source[0] == (byte)'<')
        {
            var text = Encoding.UTF8.GetString(source);
            foreach (var (oldPath, newPath) in mapping)
            {
                text = text.Replace($"\"{oldPath}\"", $"\"{newPath}\"", StringComparison.Ordinal);
            }
            return Encoding.UTF8.GetBytes(text);
        }

        MemoryStream? output = null;
        var cursor = 0;
        Span<byte> encodedLength = stackalloc byte[sizeof(uint)];
        for (var offset = sizeof(uint); offset <= source.Length - "objects/"u8.Length; offset++)
        {
            if (!source.AsSpan(offset).StartsWith("objects/"u8))
            {
                continue;
            }

            var lengthValue =
                BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset - sizeof(uint)));
            if (lengthValue > int.MaxValue)
            {
                continue;
            }

            var length = (int)lengthValue;
            if (length == 0 || offset + length > source.Length)
            {
                continue;
            }

            var oldPath = Encoding.UTF8.GetString(source, offset, length);
            if (!mapping.TryGetValue(oldPath, out var newPath))
            {
                continue;
            }

            output ??= new MemoryStream(source.Length);
            output.Write(source, cursor, offset - sizeof(uint) - cursor);
            var newPathBytes = Encoding.UTF8.GetBytes(newPath);
            BinaryPrimitives.WriteUInt32LittleEndian(encodedLength, (uint)newPathBytes.Length);
            output.Write(encodedLength);
            output.Write(newPathBytes);
            cursor = offset + length;
            offset = cursor - 1;
        }

        if (output is null)
        {
            return source;
        }

        output.Write(source, cursor, source.Length - cursor);
        return output.ToArray();
    }

    private static void WriteChestFamily(AssetWriter output, ArchiveSet source, Family family, List<string> log)
    {
        var required = new[] { family.BodyDisplayList, family.LidDisplayList, family.Front, family.Side };
        var missing = required.FirstOrDefault(path => !source.Contains(path));
        if (missing is not null)
        {
            log.Add($"Skipped {family.Name}: {missing} is missing.");
            return;
        }

        foreach (var category in Categories)
        {
            if (!ChestArt.TryGetValue(category.Name, out var art))
            {
                continue;
            }

            var frontName = AssetPath(family, category, "FrontTex");
            var sideName = AssetPath(family, category, "SideTex");
            var front = ArtTexture(source.Read(family.Front), art.Front);
            var side = ArtTexture(source.Read(family.Side), art.Side);
            output.Emit(frontName, front);
            output.Emit(sideName, side);

            var mapping = new Dictionary<string, string>
            {
                [family.Front] = frontName,
                [family.Side] = sideName,
            };
            var bodyName = AssetPath(family, category, "BodyDL");
            var lidName = AssetPath(family, category, "LidDL");
            output.Emit(bodyName, SwapHashes(source.Read(family.BodyDisplayList),
                WithMapping(mapping, family.BodyDisplayList, bodyName)));
            output.Emit(lidName, SwapHashes(source.Read(family.LidDisplayList),
                WithMapping(mapping, family.LidDisplayList, lidName)));
        }
    }

    // Index order matches the song-event song ids the seed assigns (0-19); Pad is extra.
    private static readonly string[] SongTagNames =
    [
        "Lullaby", "Epona", "Saria", "Storms", "Sun", "Time", "Minuet", "Bolero", "Serenade",
        "Requiem", "Nocturne", "Prelude", "Healing", "Soaring", "Awakening", "Goron", "GoronHalf",
        "Zora", "Emptiness", "Order", "Pad",
    ];

    private static void WriteSongTagAssets(
        AssetWriter output, ArchiveSet source, Family chestFamily, List<string> log)
    {
        if (!source.Contains(chestFamily.Front))
        {
            log.Add("Skipped song tags: the chest texture template is missing.");
            return;
        }

        var template = TextureEntry.Parse(source.Read(chestFamily.Front));
        foreach (var name in SongTagNames)
        {
            var fileName = char.ToLowerInvariant(name[0]) + name[1..];
            var art = PngArt.Load($"song_tags/{fileName}.png");
            var header = (byte[])template.Header.Clone();
            WriteUInt32(header, 0x40, 8); // grayscale-alpha 8bpp: IA8, one byte per texel
            WriteUInt32(header, 0x44, (uint)art.Width);
            WriteUInt32(header, 0x48, (uint)art.Height);
            WriteUInt32(header, 0x4c, (uint)(art.Width * art.Height));
            output.Emit($"objects/ootmm_song_tags/gSongTag{name}Tex", [.. header, .. ToIa8(art.Pixels)]);
        }

        log.Add($"Baked {SongTagNames.Length} song tag textures.");
    }

    private const string MenuDirectory = "objects/ootmm_menu";
    private const int MenuGlyphWidth = 8;
    private const int MenuGlyphHeight = 12;

    private static readonly (string File, string Name)[] MenuIcons =
    [
        ("key", "Key"), ("boss_key", "BossKey"), ("map", "Map"), ("compass", "Compass"), ("fairy", "Fairy"),
        ("skull", "Skull"), ("triforce", "Triforce"), ("rupee", "Rupee"), ("coin", "Coin"), ("sun", "Sun"),
        ("moon", "Moon"),
    ];

    private static void WriteMenuAssets(AssetWriter output, ArchiveSet source, bool isOot, List<string> log)
    {
        var chest = Families(isOot).First(family => family.Mode == FamilyMode.Chest);
        if (!source.Contains(chest.Front))
        {
            log.Add("Skipped the info menu art: the chest texture template is missing.");
            return;
        }

        var template = TextureEntry.Parse(source.Read(chest.Front));
        var font = PngArt.Load("menu/font_8x12.png");
        var columns = font.Width / MenuGlyphWidth;
        var glyphs = columns * (font.Height / MenuGlyphHeight);
        for (var index = 0; index < glyphs; index++)
        {
            var pixels = new byte[MenuGlyphWidth * MenuGlyphHeight];
            for (var row = 0; row < MenuGlyphHeight; row++)
            {
                for (var column = 0; column < MenuGlyphWidth; column++)
                {
                    var x = index % columns * MenuGlyphWidth + column;
                    var y = index / columns * MenuGlyphHeight + row;
                    pixels[row * MenuGlyphWidth + column] = font.Pixels[(y * font.Width + x) * 4] >= 128 ? (byte)0xff : (byte)0;
                }
            }
            output.Emit($"{MenuDirectory}/gMenuGlyph{index + 0x20:X2}Tex",
                MenuTexture(template, 8, MenuGlyphWidth, MenuGlyphHeight, pixels));
        }

        foreach (var (file, name) in MenuIcons)
        {
            var art = PngArt.Load($"menu/small_icon_{file}.png");
            output.Emit($"{MenuDirectory}/gMenuIcon{name}Tex", MenuTexture(template, 2, art.Width, art.Height, ToRgba16(art.Pixels)));
        }

        log.Add($"Baked {glyphs} info menu glyphs and {MenuIcons.Length} icons.");
    }

    private static byte[] MenuTexture(TextureEntry template, uint type, int width, int height, byte[] payload)
    {
        var header = (byte[])template.Header.Clone();
        WriteUInt32(header, 0x40, type);
        WriteUInt32(header, 0x44, (uint)width);
        WriteUInt32(header, 0x48, (uint)height);
        WriteUInt32(header, 0x4c, (uint)payload.Length);
        return [.. header, .. payload];
    }

    private static byte[] ToIa8(byte[] rgba)
    {
        var result = new byte[rgba.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            var luma = (rgba[i * 4] * 299 + rgba[i * 4 + 1] * 587 + rgba[i * 4 + 2] * 114) / 1000;
            result[i] = (byte)(((luma / 17) << 4) | (rgba[i * 4 + 3] / 17));
        }

        return result;
    }

    private static Dictionary<string, string> WithMapping(
        Dictionary<string, string> source, string oldPath, string newPath)
    {
        var result = new Dictionary<string, string>(source) { [oldPath] = newPath };
        return result;
    }

    private static string AssetPath(Family family, Category category, string suffix) =>
        $"{OutputDirectory}/gCsmc{family.Name}{category.Name}{suffix}";

    private static void WriteFamilyCategory(AssetWriter output, ArchiveSet source,
                                            Dictionary<ulong, string> byHash, Family family,
                                            Category category, byte[] displayList, List<string> log)
    {
        var displayListName = AssetPath(family, category, "DL");
        var mapping = new Dictionary<string, string> { [family.DisplayList] = displayListName };

        try
        {
            switch (family.Mode)
            {
                case FamilyMode.Tint:
                    var textures = GfxStreamReferences(displayList, byHash)
                        .Where(path => !path.Contains("Vtx", StringComparison.Ordinal) && source.Contains(path))
                        .ToList();
                    if (textures.Count == 0)
                    {
                        throw new InvalidDataException("display list has no resolvable textures");
                    }

                    for (var i = 0; i < textures.Count; i++)
                    {
                        var name = AssetPath(family, category, $"Tex{i}");
                        output.Emit(name, TintTexture(source.Read(textures[i]), category));
                        mapping[textures[i]] = name;
                    }
                    break;

                case FamilyMode.TintPrimitive:
                case FamilyMode.SkulltulaTint:
                    break;

                case FamilyMode.Pot:
                    var potArt = PotArt[category.Name];
                    AddArtTexture(output, source, mapping, family.Side,
                        AssetPath(family, category, "SideTex"), potArt.Side);
                    AddArtTexture(output, source, mapping, family.Top,
                        AssetPath(family, category, "TopTex"), potArt.Top);
                    break;

                case FamilyMode.Barrel:
                    var barrelName = AssetPath(family, category, "Tex");
                    output.Emit(barrelName, ArtTexture(source.Read(family.Rgba), PotArt[category.Name].Side));
                    mapping[family.Side] = barrelName;
                    mapping[family.Top] = barrelName;
                    break;

                case FamilyMode.Crate:
                    if (!source.Contains(family.Tlut) || family.Ci.Count != 2
                        || family.Ci.Any(path => !source.Contains(path)))
                    {
                        throw new InvalidDataException("crate CI or TLUT resources are missing");
                    }

                    var crateName = AssetPath(family, category, "Tex");
                    output.Emit(crateName, ArtTextureFromCi4(source.Read(family.Ci[0]), CrateArt(category.Name, false)));
                    foreach (var path in family.Ci)
                    {
                        mapping[path] = crateName;
                    }
                    break;

                case FamilyMode.SmallCrate:
                    AddArtTexture(output, source, mapping, family.Rgba,
                        AssetPath(family, category, "Tex"), CrateArt(category.Name, true));
                    break;

                default:
                    throw new InvalidDataException($"Unsupported CSMC family mode {family.Mode}.");
            }

            var baked = SwapHashes(displayList, mapping);
            baked = family.Mode switch
            {
                FamilyMode.TintPrimitive => RecolorPrimitive(baked,
                    family.SpiderShade is { } shade && category.Name == "Spider"
                        ? category with { Red = shade, Green = shade, Blue = shade }
                        : category,
                    family.Primitive, family.LeavesOnly),
                FamilyMode.SkulltulaTint => RecolorSkulltula(baked, SkulltulaColors[category.Name]),
                FamilyMode.Crate => RewriteCrateDisplayList(baked, PathCrc64(family.Tlut)),
                FamilyMode.Barrel => RewriteBarrelDisplayList(baked),
                _ => baked,
            };
            output.Emit(displayListName, baked);
        }
        catch (InvalidDataException ex)
        {
            log.Add($"Skipped {family.Name} {category.Name}: {ex.Message}.");
        }
    }

    private static void AddArtTexture(AssetWriter output, ArchiveSet source,
                                      Dictionary<string, string> mapping, string oldPath,
                                      string newPath, string artPath)
    {
        if (!source.TryRead(oldPath, out var template))
        {
            throw new InvalidDataException($"{oldPath} is missing");
        }

        output.Emit(newPath, ArtTexture(template, artPath));
        mapping[oldPath] = newPath;
    }

    private static string CrateArt(string category, bool small) => category switch
    {
        "BossKey" => "crates/boss_key.png",
        "Major" => small ? "crates/major_small.png" : "crates/major.png",
        _ => ChestArt[category].Front,
    };

    private static IReadOnlyList<Family> Families(bool isOot) => isOot
        ?
        [
            new("Bush", FamilyMode.Tint, "objects/gameplay_field_keep/gFieldBushDL"),
            new("Grass", FamilyMode.Tint, "objects/object_kusa/object_kusa_DL_000140"),
            new("Rock", FamilyMode.Tint, "objects/gameplay_field_keep/gFieldKakeraDL"),
            new("Pot", FamilyMode.Pot, "objects/object_tsubo/object_tsubo_DL_0017C0")
            {
                Side = "objects/object_tsubo/object_tsubo_Tex_000000",
                Top = "objects/object_tsubo/object_tsubo_Tex_001000",
            },
            new("Crate", FamilyMode.Crate, "objects/object_kibako2/gLargeCrateDL")
            {
                Ci =
                [
                    "objects/object_kibako2/gLargeCrateTex",
                    "objects/object_kibako2/gLargeCrateFragment1Tex",
                ],
                Tlut = "objects/object_kibako2/gLargeCrate1TLUT",
            },
            new("SmallCrate", FamilyMode.SmallCrate, "objects/gameplay_dangeon_keep/gSmallWoodenBoxDL")
            {
                Rgba = "objects/gameplay_dangeon_keep/gameplay_dangeon_keepTex_011CA0",
            },
            new("GsBody", FamilyMode.SkulltulaTint, "objects/object_st/object_st_DL_003FB0"),
            new("GsHead", FamilyMode.SkulltulaTint, "objects/object_st/object_st_DL_0043D8"),
            new("CowRing", FamilyMode.TintPrimitive, "objects/object_cow/gCowNoseRingDL")
            {
                Primitive = 0xffffffff,
                Extra = [new("Normal", 0x30, 0x0f, 0x02), new("Renewable", 0x00, 0xff, 0x00)],
            },
            new("Chest", FamilyMode.Chest)
            {
                Side = "objects/object_box/gTreasureChestSideAndTopTex",
                Front = "objects/object_box/gTreasureChestFrontTex",
                BodyDisplayList = "objects/object_box/gTreasureChestChestFrontDL",
                LidDisplayList = "objects/object_box/gTreasureChestChestSideAndLidDL",
            },
        ]
        :
        [
            new("Bush", FamilyMode.Tint, "objects/gameplay_field_keep/gKusaBushType1DL"),
            new("Grass", FamilyMode.Tint, "objects/object_kusa/gKusaSproutDL"),
            new("RockOpa", FamilyMode.Tint, "objects/gameplay_field_keep/gFieldSmallRockOpaDL"),
            new("RockXlu", FamilyMode.Tint, "objects/gameplay_field_keep/gFieldSmallRockXluDL"),
            new("RockIshi", FamilyMode.Tint, "objects/object_ishi/gSmallRockDL"),
            new("Tree", FamilyMode.Tint, "objects/object_tree/gTreeLeavesDL"),
            new("Wood02", FamilyMode.TintPrimitive, "objects/object_wood02/object_wood02_DL_007968")
            {
                Primitive = 0x1c580dff,
            },
            new("Wood02Bush", FamilyMode.TintPrimitive, "objects/object_wood02/object_wood02_DL_000090")
            {
                Primitive = 0x669870ff,
            },
            new("Snowball", FamilyMode.TintPrimitive, "objects/object_goroiwa/object_goroiwa_DL_008B90")
            {
                Primitive = 0xffffffff,
                SpiderShade = 0x90,
            },
            new("SnowTree", FamilyMode.TintPrimitive, "objects/object_snowwd/gSnowTreeDL")
            {
                Primitive = 0xffffffff,
                SpiderShade = 0x90,
                LeavesOnly = true,
            },
            new("FlowerpotPot", FamilyMode.Tint, "objects/object_flowerpot/object_flowerpot_DL_0012E0"),
            new("FlowerpotGrass", FamilyMode.Tint, "objects/object_flowerpot/object_flowerpot_DL_001408"),
            new("Pot", FamilyMode.Pot, "objects/object_tsubo/gPotDL")
            {
                Side = "objects/object_tsubo/gPotWallTex",
                Top = "objects/object_tsubo/gPotRimTex",
            },
            new("Crate", FamilyMode.Crate, "objects/object_kibako2/gLargeCrateDL")
            {
                Ci =
                [
                    "objects/object_kibako2/gLargeCrateSidesTex",
                    "objects/object_kibako2/gLargeCrateTopTex",
                ],
                Tlut = "objects/object_kibako2/gLargeCrateTLUT",
            },
            new("SmallCrate", FamilyMode.SmallCrate, "objects/object_kibako/gSmallCrateDL")
            {
                Rgba = "objects/object_kibako/gSmallCrateTex",
            },
            new("Barrel", FamilyMode.Barrel, "objects/object_taru/gObjTaruBarrelDL")
            {
                Side = "objects/object_taru/gObjTaruBarrelSideTex",
                Top = "objects/object_taru/gObjTaruBarrelTopTex",
                Rgba = "objects/object_tsubo/gPotWallTex",
            },
            new("GsBody", FamilyMode.SkulltulaTint, "objects/object_st/object_st_DL_003FB0"),
            new("GsHead", FamilyMode.SkulltulaTint, "objects/object_st/object_st_DL_0043D8"),
            new("CowRing", FamilyMode.TintPrimitive, "objects/object_cow/gCowNoseRingDL")
            {
                Primitive = 0xffffffff,
                Extra = [new("Normal", 0x30, 0x0f, 0x02), new("Renewable", 0x00, 0xff, 0x00)],
            },
            new("Chest", FamilyMode.Chest)
            {
                Side = "objects/object_box/gBoxChestCornerTex",
                Front = "objects/object_box/gBoxChestLockTex",
                BodyDisplayList = "objects/object_box/gBoxChestBaseDL",
                LidDisplayList = "objects/object_box/gBoxChestLidDL",
            },
        ];

    private static byte[] TintTexture(byte[] entry, Category category)
    {
        var texture = TextureEntry.Parse(entry);
        if (texture.Type != 2 || texture.Payload.Length != texture.Width * texture.Height * 2)
        {
            throw new InvalidDataException("texture is not RGBA16");
        }

        var lut = new int[32];
        for (var level = 1; level < lut.Length; level++)
        {
            var value = Math.Pow((level * 255 / 31) / 255.0, 0.25) * 255.0;
            lut[level] = (int)Math.Round(value, MidpointRounding.ToEven);
        }

        var pixels = (byte[])texture.Payload.Clone();
        for (var i = 0; i < pixels.Length; i += 2)
        {
            var pixel = (pixels[i] << 8) | pixels[i + 1];
            var light = lut[Math.Max((pixel >> 11) & 31, Math.Max((pixel >> 6) & 31, (pixel >> 1) & 31))];
            var tinted = (((light * category.Red / 255) * 31 / 255) << 11)
                         | (((light * category.Green / 255) * 31 / 255) << 6)
                         | (((light * category.Blue / 255) * 31 / 255) << 1)
                         | (pixel & 1);
            pixels[i] = (byte)(tinted >> 8);
            pixels[i + 1] = (byte)tinted;
        }

        return texture.Join(pixels);
    }

    private static byte[] ArtTexture(byte[] template, string artPath)
    {
        var texture = TextureEntry.Parse(template);
        if (texture.Type != 2 || texture.Payload.Length != texture.Width * texture.Height * 2)
        {
            throw new InvalidDataException("texture template is not RGBA16");
        }

        var art = PngArt.Load(artPath);
        ValidateArtDimensions(artPath, art, texture);
        return texture.Join(ToRgba16(art.Pixels));
    }

    private static byte[] ArtTextureFromCi4(byte[] template, string artPath)
    {
        var texture = TextureEntry.Parse(template);
        if (texture.Type != 3 || texture.Payload.Length != texture.Width * texture.Height / 2)
        {
            throw new InvalidDataException("texture template is not CI4");
        }

        var art = PngArt.Load(artPath);
        ValidateArtDimensions(artPath, art, texture);
        var pixels = ToRgba16(art.Pixels);
        var header = (byte[])texture.Header.Clone();
        WriteUInt32(header, 0x40, 2);
        WriteUInt32(header, 0x44, (uint)texture.Width);
        WriteUInt32(header, 0x48, (uint)texture.Height);
        WriteUInt32(header, 0x4c, (uint)pixels.Length);
        return [.. header, .. pixels];
    }

    private static void ValidateArtDimensions(string name, PngArt art, TextureEntry texture)
    {
        if (art.Width != texture.Width || art.Height != texture.Height)
        {
            throw new InvalidDataException(
                $"{name} is {art.Width}x{art.Height}; expected {texture.Width}x{texture.Height}");
        }
    }

    private static byte[] ToRgba16(byte[] rgba)
    {
        var result = new byte[rgba.Length / 2];
        for (int source = 0, target = 0; source < rgba.Length; source += 4, target += 2)
        {
            var pixel = ((rgba[source] * 31 / 255) << 11)
                        | ((rgba[source + 1] * 31 / 255) << 6)
                        | ((rgba[source + 2] * 31 / 255) << 1)
                        | (rgba[source + 3] >= 128 ? 1 : 0);
            result[target] = (byte)(pixel >> 8);
            result[target + 1] = (byte)pixel;
        }

        return result;
    }

    private static IReadOnlyList<string> GfxStreamReferences(byte[] data, Dictionary<ulong, string> byHash)
    {
        var result = new List<string>();
        var offset = 0x58;
        while (offset + 8 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var word1 = ReadUInt32(data, offset + 4);
            var expanded = false;
            foreach (var operation in new[] { word0, word1 })
            {
                if (!IsExpandedOperation(operation) || offset + 16 > data.Length)
                {
                    continue;
                }

                if (IsHashOperation(operation))
                {
                    var first = ReadUInt32(data, offset + 8);
                    var second = ReadUInt32(data, offset + 12);
                    foreach (var hash in new[] { ((ulong)second << 32) | first, ((ulong)first << 32) | second })
                    {
                        if (byHash.TryGetValue(hash, out var path) && !result.Contains(path))
                        {
                            result.Add(path);
                        }
                    }
                }

                offset += 16;
                expanded = true;
                break;
            }

            if (expanded)
            {
                continue;
            }

            if ((word0 >> 24) == 0xdf || (word1 >> 24) == 0xdf)
            {
                break;
            }

            offset += 8;
        }

        return result;
    }

    private static byte[] SwapHashes(byte[] source, IReadOnlyDictionary<string, string> mapping)
    {
        var result = (byte[])source.Clone();
        foreach (var (oldPath, newPath) in mapping)
        {
            var oldEncodings = HashEncodings(PathCrc64(oldPath));
            var newEncodings = HashEncodings(PathCrc64(newPath));
            for (var i = 0; i < oldEncodings.Length; i++)
            {
                ReplaceAll(result, oldEncodings[i], newEncodings[i]);
            }
        }

        return result;
    }

    private static byte[][] HashEncodings(ulong hash)
    {
        var high = (uint)(hash >> 32);
        var low = (uint)hash;
        var words = new byte[8];
        var little = new byte[8];
        var big = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(words, high);
        BinaryPrimitives.WriteUInt32LittleEndian(words.AsSpan(4), low);
        BinaryPrimitives.WriteUInt64LittleEndian(little, hash);
        BinaryPrimitives.WriteUInt64BigEndian(big, hash);
        return [words, little, big];
    }

    private static void ReplaceAll(byte[] data, byte[] search, byte[] replacement)
    {
        for (var offset = 0; offset <= data.Length - search.Length;)
        {
            if (data.AsSpan(offset, search.Length).SequenceEqual(search))
            {
                replacement.CopyTo(data, offset);
                offset += search.Length;
            }
            else
            {
                offset++;
            }
        }
    }

    private static byte[] RewriteCrateDisplayList(byte[] data, ulong tlutHash)
    {
        var tlutEncodings = HashEncodings(tlutHash);
        var textureLoads = 0;
        var removedTlut = false;
        var offset = 0x58;
        while (offset + 8 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var word1 = ReadUInt32(data, offset + 4);
            var operation = word0 >> 24;
            if (operation == 0xdf)
            {
                break;
            }

            if (operation == 0x20)
            {
                if (offset + 16 > data.Length)
                {
                    throw new InvalidDataException("crate display list ends inside a resource command");
                }

                var hash = data.AsSpan(offset + 8, 8);
                var isTlut = false;
                foreach (var encoding in tlutEncodings)
                {
                    if (hash.SequenceEqual(encoding))
                    {
                        isTlut = true;
                        break;
                    }
                }
                if (isTlut)
                {
                    var cursor = offset + 16;
                    var ended = false;
                    while (cursor + 8 <= data.Length)
                    {
                        var nested = data[cursor + 3];
                        if (nested is not (0xe8 or 0xf5 or 0xe6 or 0xf0))
                        {
                            throw new InvalidDataException("crate TLUT command sequence is incompatible");
                        }

                        Array.Clear(data, cursor, 8);
                        cursor += 8;
                        if (nested == 0xf0)
                        {
                            ended = true;
                            break;
                        }
                    }

                    if (!ended)
                    {
                        throw new InvalidDataException("crate TLUT command sequence is incomplete");
                    }

                    Array.Clear(data, offset, 16);
                    removedTlut = true;
                    offset = cursor;
                    continue;
                }

                WriteUInt32(data, offset, 0x20100000);
                textureLoads++;
                offset += 16;
                continue;
            }

            if (operation == 0xe3 && (word0 & 0x0000ff00) == 0x00001000)
            {
                WriteUInt32(data, offset + 4, 0);
            }
            else if (operation == 0xf5 && (word1 >> 24) == 0x07)
            {
                WriteUInt32(data, offset, 0xf5100000);
                WriteUInt32(data, offset + 4, 0x0709815f);
            }
            else if (operation == 0xf3)
            {
                WriteUInt32(data, offset + 4, 0x077ff100);
            }
            else if (operation == 0xf5)
            {
                WriteUInt32(data, offset, 0xf5101000);
                WriteUInt32(data, offset + 4, 0x0009815f);
            }
            else if (IsExpandedOperation(word0))
            {
                offset += 16;
                continue;
            }

            offset += 8;
        }

        if (textureLoads != 2 || !removedTlut)
        {
            throw new InvalidDataException("crate display list material is incompatible");
        }

        return data;
    }

    private static byte[] RewriteBarrelDisplayList(byte[] data)
    {
        var textureLoads = 0;
        var offset = 0x58;
        while (offset + 72 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var operation = word0 >> 24;
            if (operation == 0xdf)
            {
                break;
            }

            if (operation != 0x20)
            {
                offset += IsExpandedOperation(word0) ? 16 : 8;
                continue;
            }

            WriteUInt32(data, offset, 0x20100000);
            WriteUInt32(data, offset + 16, 0xf5100000);
            WriteUInt32(data, offset + 20, 0x07018050);
            WriteUInt32(data, offset + 32, 0xf3000000);
            WriteUInt32(data, offset + 36, 0x077ff100);
            WriteUInt32(data, offset + 48, 0xf5101000);
            WriteUInt32(data, offset + 52, 0x00018050);
            WriteUInt32(data, offset + 56, 0xf2000000);
            WriteUInt32(data, offset + 60, 0x0007c0fc);
            if ((ReadUInt32(data, offset + 64) >> 24) == 0xf2)
            {
                WriteUInt32(data, offset + 64, 0xf2000000);
                WriteUInt32(data, offset + 68, 0x0007c0fc);
            }

            textureLoads++;
            offset += 64;
        }

        if (textureLoads != 4)
        {
            throw new InvalidDataException("barrel display list material is incompatible");
        }

        return data;
    }

    private static byte[] RecolorSkulltula(byte[] data, (uint Primitive, uint Environment) colors)
    {
        const uint goldPrimitive = 0xf0dc1eff;
        const uint goldEnvironment = 0x141400ff;
        var primitives = 0;
        var environments = 0;
        var offset = 0x58;
        while (offset + 8 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var operation = word0 >> 24;
            if (operation == 0xdf)
            {
                break;
            }

            if (operation == 0xfa && ReadUInt32(data, offset + 4) == goldPrimitive)
            {
                WriteUInt32(data, offset + 4, colors.Primitive);
                primitives++;
            }
            else if (operation == 0xfb && ReadUInt32(data, offset + 4) == goldEnvironment)
            {
                WriteUInt32(data, offset + 4, colors.Environment);
                environments++;
            }

            offset += IsExpandedOperation(word0) ? 16 : 8;
        }

        if (primitives != 1 || environments != 1)
        {
            throw new InvalidDataException("display list does not contain the gold skulltula colors");
        }

        return data;
    }

    private static byte[] RecolorPrimitive(byte[] data, Category category, uint expectedColor, bool leavesOnly = false)
    {
        var writes = 0;
        uint primitiveWord = 0;
        var drewLeaves = false;
        var restored = !leavesOnly;
        var offset = 0x58;
        while (offset + 8 <= data.Length)
        {
            var word0 = ReadUInt32(data, offset);
            var operation = word0 >> 24;
            if (operation == 0xdf)
            {
                break;
            }

            if (operation == 0xfa && ReadUInt32(data, offset + 4) == expectedColor)
            {
                WriteUInt32(data, offset + 4,
                    ((uint)category.Red << 24) | ((uint)category.Green << 16) | ((uint)category.Blue << 8) | 0xff);
                primitiveWord = word0;
                writes++;
            }
            else if (writes == 1 && operation is 0x05 or 0x06 or 0x07)
            {
                drewLeaves = true;
            }
            else if (!restored && drewLeaves && operation == 0xe7)
            {
                WriteUInt32(data, offset, primitiveWord);
                WriteUInt32(data, offset + 4, expectedColor);
                restored = true;
            }

            offset += IsExpandedOperation(word0) ? 16 : 8;
        }

        if (writes != 1)
        {
            throw new InvalidDataException("display list does not contain the expected primitive color");
        }

        if (!restored)
        {
            throw new InvalidDataException("display list has no material after the recolored leaves");
        }

        return data;
    }

    private static bool IsHashOperation(uint word) => (word >> 24) is 0x20 or 0x31 or 0x32;

    private static bool IsExpandedOperation(uint word) =>
        (word >> 24) is 0x20 or 0x31 or 0x32 or 0x33 or 0x35 or 0x36 or 0x42;

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private static ulong PathCrc64(string path)
    {
        var result = ulong.MaxValue;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(path))
        {
            result = Crc64Table[(byte)(result >> 56) ^ value] ^ (result << 8);
        }
        return result;
    }

    private static ulong[] CreateCrc64Table()
    {
        const ulong polynomial = 0x42f0e1eba9ea3693;
        var table = new ulong[256];
        for (var i = 0; i < table.Length; i++)
        {
            var crc = (ulong)i << 56;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000000000000000) != 0 ? (crc << 1) ^ polynomial : crc << 1;
            }
            table[i] = crc;
        }
        return table;
    }

    private sealed class ArchiveSet : IDisposable
    {
        private readonly List<ZipArchive> _archives = [];
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);

        public ArchiveSet(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                var archive = ZipFile.OpenRead(path);
                _archives.Add(archive);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length > 0)
                    {
                        _entries.TryAdd(entry.FullName.Replace('\\', '/'), entry);
                    }
                }
            }
        }

        public IEnumerable<string> Names => _entries.Keys;

        public bool Contains(string path) => _entries.ContainsKey(path);

        public bool TryRead(string path, out byte[] data)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                data = [];
                return false;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            data = memory.ToArray();
            return true;
        }

        public byte[] Read(string path) => TryRead(path, out var data)
            ? data
            : throw new InvalidDataException($"Archive entry {path} is missing.");

        public void Dispose()
        {
            foreach (var archive in _archives)
            {
                archive.Dispose();
            }
        }
    }

    private sealed class AssetWriter(ZipArchive archive)
    {
        private readonly HashSet<string> _written = new(StringComparer.Ordinal);

        public int Count => _written.Count;

        public void Emit(string path, byte[] data)
        {
            if (!_written.Add(path))
            {
                return;
            }

            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data);
        }
    }

    private sealed record TextureEntry(byte[] Header, byte[] Payload, int Type, int Width, int Height)
    {
        public static TextureEntry Parse(byte[] entry)
        {
            if (entry.Length < 0x50)
            {
                throw new InvalidDataException("texture entry is truncated");
            }

            var type = checked((int)ReadUInt32(entry, 0x40));
            var width = checked((int)ReadUInt32(entry, 0x44));
            var height = checked((int)ReadUInt32(entry, 0x48));
            var length = checked((int)ReadUInt32(entry, 0x4c));
            if (length <= 0 || length > entry.Length)
            {
                throw new InvalidDataException("texture payload length is invalid");
            }

            return new TextureEntry(entry[..^length], entry[^length..], type, width, height);
        }

        public byte[] Join(byte[] payload) => [.. Header, .. payload];
    }

    private sealed record PngArt(int Width, int Height, byte[] Pixels)
    {
        public static PngArt Load(string relativePath)
        {
            var assembly = typeof(OotmmAssetBuilder).Assembly;
            var suffix = ".Assets.Csmc." + relativePath.Replace('/', '.');
            var resource = assembly.GetManifestResourceNames()
                .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resource is null)
            {
                throw new InvalidDataException($"Embedded OoTMM art {relativePath} is missing");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                               ?? throw new InvalidDataException($"Cannot read embedded OoTMM art {relativePath}");
            return Decode(stream, relativePath);
        }

        private static PngArt Decode(Stream source, string name)
        {
            using var reader = new BinaryReader(source);
            if (!reader.ReadBytes(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException($"{name} is not a PNG image");
            }

            var width = 0;
            var height = 0;
            var colorType = -1;
            using var compressed = new MemoryStream();
            while (source.Position < source.Length)
            {
                var length = ReadBigEndianInt(reader);
                var type = new string(reader.ReadChars(4));
                var data = reader.ReadBytes(length);
                reader.ReadUInt32();
                if (data.Length != length)
                {
                    throw new InvalidDataException($"{name} contains a truncated PNG chunk");
                }

                if (type == "IHDR")
                {
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
                    if (data[8] != 8 || data[9] is not (2 or 6) || data[12] != 0)
                    {
                        throw new InvalidDataException($"{name} uses an unsupported PNG format");
                    }
                    colorType = data[9];
                }
                else if (type == "IDAT")
                {
                    compressed.Write(data);
                }
                else if (type == "IEND")
                {
                    break;
                }
            }

            if (width <= 0 || height <= 0 || colorType < 0)
            {
                throw new InvalidDataException($"{name} has no valid PNG header");
            }

            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            zlib.CopyTo(decoded);
            var filtered = decoded.ToArray();
            var channels = colorType == 6 ? 4 : 3;
            var stride = checked(width * channels);
            if (filtered.Length != checked((stride + 1) * height))
            {
                throw new InvalidDataException($"{name} has an invalid PNG payload length");
            }

            var raw = Unfilter(filtered, stride, height, channels);
            var rgba = new byte[checked(width * height * 4)];
            for (int input = 0, output = 0; input < raw.Length; input += channels, output += 4)
            {
                rgba[output] = raw[input];
                rgba[output + 1] = raw[input + 1];
                rgba[output + 2] = raw[input + 2];
                rgba[output + 3] = channels == 4 ? raw[input + 3] : (byte)255;
            }
            return new PngArt(width, height, rgba);
        }

        private static byte[] Unfilter(byte[] source, int stride, int height, int bytesPerPixel)
        {
            var result = new byte[stride * height];
            var sourceOffset = 0;
            for (var row = 0; row < height; row++)
            {
                var filter = source[sourceOffset++];
                var rowOffset = row * stride;
                for (var column = 0; column < stride; column++)
                {
                    var value = source[sourceOffset++];
                    var left = column >= bytesPerPixel ? result[rowOffset + column - bytesPerPixel] : 0;
                    var above = row > 0 ? result[rowOffset + column - stride] : 0;
                    var upperLeft = row > 0 && column >= bytesPerPixel
                        ? result[rowOffset + column - stride - bytesPerPixel]
                        : 0;
                    result[rowOffset + column] = filter switch
                    {
                        0 => value,
                        1 => (byte)(value + left),
                        2 => (byte)(value + above),
                        3 => (byte)(value + ((left + above) / 2)),
                        4 => (byte)(value + Paeth(left, above, upperLeft)),
                        _ => throw new InvalidDataException($"Unsupported PNG filter {filter}"),
                    };
                }
            }
            return result;
        }

        private static int Paeth(int left, int above, int upperLeft)
        {
            var estimate = left + above - upperLeft;
            var leftDistance = Math.Abs(estimate - left);
            var aboveDistance = Math.Abs(estimate - above);
            var diagonalDistance = Math.Abs(estimate - upperLeft);
            return leftDistance <= aboveDistance && leftDistance <= diagonalDistance
                ? left
                : aboveDistance <= diagonalDistance ? above : upperLeft;
        }

        private static int ReadBigEndianInt(BinaryReader reader)
        {
            Span<byte> data = stackalloc byte[4];
            if (reader.Read(data) != data.Length)
            {
                throw new InvalidDataException("PNG chunk header is truncated");
            }
            return checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
        }
    }

    private sealed record Category(string Name, byte Red, byte Green, byte Blue);

    private enum FamilyMode
    {
        Tint,
        TintPrimitive,
        SkulltulaTint,
        Pot,
        Barrel,
        Crate,
        SmallCrate,
        Chest,
    }

    private sealed record Family(string Name, FamilyMode Mode, string DisplayList = "")
    {
        public string Side { get; init; } = "";
        public string Top { get; init; } = "";
        public IReadOnlyList<string> Ci { get; init; } = [];
        public string Tlut { get; init; } = "";
        public string Rgba { get; init; } = "";
        public string BodyDisplayList { get; init; } = "";
        public string LidDisplayList { get; init; } = "";
        public string Front { get; init; } = "";
        public uint Primitive { get; init; }
        public byte? SpiderShade { get; init; }
        public bool LeavesOnly { get; init; }
        public IReadOnlyList<Category> Extra { get; init; } = [];
    }
}
