using System.Buffers.Binary;
using System.IO.Compression;
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
        Assert.Equal(56, result.Entries);
        Assert.False(File.Exists(ootLegacy));
        Assert.False(File.Exists(mmLegacy));
        AssertAssetArchive(Path.Combine(oot.Directory, "mods", OotmmAssetBuilder.ArchiveName));
        AssertAssetArchive(Path.Combine(mm.Directory, "mods", OotmmAssetBuilder.ArchiveName));
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

    private static void AssertAssetArchive(string path)
    {
        Assert.True(File.Exists(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(28, archive.Entries.Count);
        Assert.All(archive.Entries,
            entry => Assert.StartsWith("objects/ootmm_csmc/", entry.FullName, StringComparison.Ordinal));
        Assert.Contains(archive.Entries,
            entry => entry.FullName.EndsWith("gCsmcChestMajorBodyDL", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries,
            entry => entry.FullName.Contains("object_gi_", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, byte[]> OotChestEntries() => new Dictionary<string, byte[]>
    {
        ["objects/object_box/gTreasureChestChestFrontDL"] = DisplayList(),
        ["objects/object_box/gTreasureChestChestSideAndLidDL"] = DisplayList(),
        ["objects/object_box/gTreasureChestFrontTex"] = Rgba16Texture(32, 64),
        ["objects/object_box/gTreasureChestSideAndTopTex"] = Rgba16Texture(32, 32),
    };

    private static IReadOnlyDictionary<string, byte[]> MmChestEntries() => new Dictionary<string, byte[]>
    {
        ["objects/object_box/gBoxChestBaseDL"] = DisplayList(),
        ["objects/object_box/gBoxChestLidDL"] = DisplayList(),
        ["objects/object_box/gBoxChestLockTex"] = Rgba16Texture(32, 64),
        ["objects/object_box/gBoxChestCornerTex"] = Rgba16Texture(32, 32),
    };

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
