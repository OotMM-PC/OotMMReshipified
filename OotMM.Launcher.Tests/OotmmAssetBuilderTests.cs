using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OotMM.Launcher.Models;

namespace OotMM.Launcher.Tests;

public sealed class OotmmAssetBuilderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ootmm-asset-builder-tests", Guid.NewGuid().ToString("N"));

    public OotmmAssetBuilderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void GeneratedAssetsReplaceLegacyModelPacksAsRequiredMods()
    {
        Assert.True(ModScanner.IsRequired(OotmmAssetBuilder.ArchiveName));
        Assert.False(ModScanner.IsRequired("ootmm_mm_models.o2r"));
        Assert.False(ModScanner.IsRequired("ootmm_oot_models.o2r"));
    }

    [Fact]
    public void Build_CreatesTargetArchivesAndRemovesLegacyModelPacks()
    {
        var oot = CreateGame("oot", "soh.exe", "oot.o2r", "soh.o2r", OotChestEntries());
        var mm = CreateGame("mm", "2ship.exe", "mm.o2r", "2ship.o2r", MmChestEntries());
        var ootLegacy = Path.Combine(oot.Directory, "mods", "ootmm_mm_models.o2r");
        var mmLegacy = Path.Combine(mm.Directory, "mods", "ootmm_oot_models.o2r.disabled");
        File.WriteAllBytes(ootLegacy, []);
        File.WriteAllBytes(mmLegacy, []);

        Assert.True(OotmmAssetBuilder.NeedsBuild(oot.Executable, isOot: true));
        Assert.True(OotmmAssetBuilder.NeedsBuild(mm.Executable, isOot: false));

        var result = OotmmAssetBuilder.Build(oot.Executable, mm.Executable);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(69, result.Entries);
        Assert.False(File.Exists(ootLegacy));
        Assert.False(File.Exists(mmLegacy));
        AssertAssetArchive(Path.Combine(oot.Directory, "mods", OotmmAssetBuilder.ArchiveName), 34,
            "objects/mm_gameplay_keep/gStrayFairySkel");
        var mmAssets = Path.Combine(mm.Directory, "mods", OotmmAssetBuilder.ArchiveName);
        AssertAssetArchive(mmAssets, 35, "objects/ot_obj_trap/gSlidingBladeTrapDL");
        AssertTextPath(mmAssets, "objects/ot_obj_key/gSkeletonKeyDL",
            "objects/ot_obj_key/gSkeletonKeyDL_tri_0");
        AssertSerializedPath(
            Path.Combine(oot.Directory, "mods", OotmmAssetBuilder.ArchiveName),
            "objects/mm_gameplay_keep/gStrayFairySkel",
            "objects/mm_gameplay_keep/gStrayFairyFlyingAnim");
        Assert.False(OotmmAssetBuilder.NeedsBuild(oot.Executable, isOot: true));
        Assert.False(OotmmAssetBuilder.NeedsBuild(mm.Executable, isOot: false));
    }

    private (string Directory, string Executable) CreateGame(
        string directoryName, string exeName, string gameArchive, string portArchive,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(Path.Combine(directory, "mods"));
        var executable = Path.Combine(directory, exeName);
        File.WriteAllBytes(executable, []);
        WriteArchive(Path.Combine(directory, gameArchive), entries);
        WriteArchive(Path.Combine(directory, portArchive), new Dictionary<string, byte[]>());
        return (directory, executable);
    }

    private static void AssertAssetArchive(string path, int entryCount, string? requiredPath = null)
    {
        Assert.True(File.Exists(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(entryCount, archive.Entries.Count);
        Assert.Contains(archive.Entries,
            entry => entry.FullName.EndsWith("gCsmcChestMajorBodyDL", StringComparison.Ordinal));
        Assert.Contains(archive.Entries,
            entry => entry.FullName.StartsWith("objects/mm_obj_", StringComparison.Ordinal) ||
                     entry.FullName.StartsWith("objects/ot_obj_", StringComparison.Ordinal));
        if (requiredPath is not null)
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == requiredPath);
        }
        Assert.DoesNotContain(archive.Entries,
            entry => entry.FullName.Contains("object_gi_", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, byte[]> OotChestEntries() => new Dictionary<string, byte[]>
    {
        ["objects/object_box/gTreasureChestChestFrontDL"] = DisplayList(),
        ["objects/object_box/gTreasureChestChestSideAndLidDL"] = DisplayList(),
        ["objects/object_box/gTreasureChestFrontTex"] = Rgba16Texture(32, 64),
        ["objects/object_box/gTreasureChestSideAndTopTex"] = Rgba16Texture(32, 32),
        ["objects/object_key/gSkeletonKeyDL"] = Encoding.UTF8.GetBytes(
            "<DisplayList><CallDisplayList Path=\"objects/object_key/gSkeletonKeyDL_tri_0\"/></DisplayList>"),
        ["objects/object_key/gSkeletonKeyDL_tri_0"] = DisplayList(),
        ["objects/object_trap/gSlidingBladeTrapDL"] = DisplayList(),
    };

    private static IReadOnlyDictionary<string, byte[]> MmChestEntries() => new Dictionary<string, byte[]>
    {
        ["objects/object_box/gBoxChestBaseDL"] = DisplayList(),
        ["objects/object_box/gBoxChestLidDL"] = DisplayList(),
        ["objects/object_box/gBoxChestLockTex"] = Rgba16Texture(32, 64),
        ["objects/object_box/gBoxChestCornerTex"] = Rgba16Texture(32, 32),
        ["objects/gameplay_keep/gStrayFairySkel"] =
            SerializedPath("objects/gameplay_keep/gStrayFairyFlyingAnim"),
        ["objects/gameplay_keep/gStrayFairyFlyingAnim"] = DisplayList(),
    };

    private static void AssertSerializedPath(string archivePath, string entryPath, string expected)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry(entryPath)!.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        var data = output.ToArray();
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data);
        Assert.Equal(expected, Encoding.UTF8.GetString(data, sizeof(uint), (int)length));
    }

    private static void AssertTextPath(string archivePath, string entryPath, string expected)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry(entryPath)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Contains($"Path=\"{expected}\"", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    private static byte[] SerializedPath(string path)
    {
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var data = new byte[sizeof(uint) + pathBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)pathBytes.Length);
        pathBytes.CopyTo(data, sizeof(uint));
        return data;
    }

    private static byte[] DisplayList()
    {
        var data = new byte[0x60];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x58), 0xdf000000);
        return data;
    }

    private static byte[] Rgba16Texture(int width, int height)
    {
        var payloadLength = width * height * 2;
        var data = new byte[0x50 + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x40), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x44), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x48), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x4c), (uint)payloadLength);
        return data;
    }

    private static void WriteArchive(string path, IReadOnlyDictionary<string, byte[]> entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(data);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
