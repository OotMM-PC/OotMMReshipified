using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;

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
        var builderStamp = File.GetLastWriteTimeUtc(typeof(OotmmAssetBuilder).Assembly.Location);
        return outputStamp < builderStamp || inputs.Any(path => outputStamp < File.GetLastWriteTimeUtc(path));
    }

    public static OotmmAssetBuildResult Build(string ootExe, string mmExe)
    {
        var log = new List<string>();
        var entries = 0;
        try
        {
            if (File.Exists(ootExe))
            {
                entries += BuildGame(ootExe, isOot: true, log);
            }
            if (File.Exists(mmExe))
            {
                entries += BuildGame(mmExe, isOot: false, log);
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

    private static int BuildGame(string exePath, bool isOot, List<string> log)
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

    private static void RemoveLegacyArchive(string exePath, string name)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(dir))
        {
            File.Delete(Path.Combine(dir, "mods", name));
            File.Delete(Path.Combine(dir, "mods", name + ".disabled"));
        }
    }

    private static void WriteCsmcAssets(AssetWriter output, ArchiveSet source, bool isOot, List<string> log)
    {
        var byHash = source.Names.ToDictionary(PathCrc64, name => name);
        foreach (var family in Families(isOot))
        {
            if (family.Mode == FamilyMode.Chest)
            {
                WriteChestFamily(output, source, family, log);
                continue;
            }

            if (!source.TryRead(family.DisplayList, out var displayList))
            {
                log.Add($"Skipped {family.Name}: {family.DisplayList} is missing.");
                continue;
            }

            foreach (var category in Categories)
            {
                WriteFamilyCategory(output, source, byHash, family, category, displayList, log);
            }
        }
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
                FamilyMode.TintPrimitive => RecolorPrimitive(baked, category),
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
            new("Wood02", FamilyMode.TintPrimitive, "objects/object_wood02/object_wood02_DL_007968"),
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

    private static byte[] RecolorPrimitive(byte[] data, Category category)
    {
        const uint expectedColor = 0x1c580dff;
        var writes = 0;
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
                writes++;
            }

            offset += IsExpandedOperation(word0) ? 16 : 8;
        }

        if (writes != 1)
        {
            throw new InvalidDataException("display list does not contain the expected primitive color");
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
    }
}
