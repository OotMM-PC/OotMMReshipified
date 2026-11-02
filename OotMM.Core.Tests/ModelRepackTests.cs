using System.IO.Compression;
using System.Text;
using OotMM.Core.Models;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

public class ModelRepackTests
{
    [Theory]
    [InlineData("objects/object_link_child/gLinkHumanSkel", 6914884263858874222UL)]
    [InlineData("p01objs/object_link_child/gLinkHumanSkel", 13930404445129331195UL)]
    [InlineData("objects/object_link_boy/gLinkAdultSkel", 17343821888806444020UL)]
    [InlineData("p0aobjs/object_link_boy/gLinkAdultSkel", 7759082645766003366UL)]
    [InlineData("", 18446744073709551615UL)]
    public void Crc64_MatchesLibultraship(string path, ulong expected) =>
        Assert.Equal(expected, Crc64.OfPath(path));

    [Fact]
    public void NamespacePrefix_IsTheSameLengthAsTheDirectoryItReplaces() =>
        Assert.Equal("objects/".Length, (ModelRepack.PlayerPrefix(7) + "objs/").Length);

    [Theory]
    [InlineData("objects/object_link_child/gLinkHumanSkel", "p03objs/object_link_child/gLinkHumanSkel")]
    [InlineData("alt/objects/object_link_boy/gLinkAdultSkel", "p03objs/object_link_boy/gLinkAdultSkel")]
    public void NamespacedPath_UnifiesAltAndCanonicalIntoOneNamespace(string input, string expected) =>
        Assert.Equal(expected, ModelRepack.NamespacedPath(input, ModelRepack.PlayerPrefix(3)));

    [Theory]
    [InlineData("objects/object_horse/gHorseSkel")]
    [InlineData("scenes/dungeons/ydan/ydan_scene")]
    [InlineData("textures/icon_item_static/gGiHeartTex")]
    public void NamespacedPath_DropsEntriesOutsideThePlayerModel(string input) =>
        Assert.Null(ModelRepack.NamespacedPath(input, ModelRepack.PlayerPrefix(3)));

    [Fact]
    public void Repack_NamespacesEntriesAndRewritesEmbeddedReferences()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string skel = "objects/object_link_child/gLinkHumanSkel";
            const string tex = "objects/object_link_child/gLinkHumanEyesTex";
            var input = Path.Combine(dir, "model.o2r");

            var body = new List<byte>();
            body.AddRange(Encoding.ASCII.GetBytes(tex));
            body.Add(0);
            var hash = Crc64.OfPath(tex);
            body.AddRange(BitConverter.GetBytes((uint)(hash >> 32)));
            body.AddRange(BitConverter.GetBytes((uint)hash));

            using (var file = File.Create(input))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var s = zip.CreateEntry(skel).Open())
                {
                    s.Write(body.ToArray());
                }

                using (var s = zip.CreateEntry(tex).Open())
                {
                    s.Write(new byte[] { 1, 2, 3, 4 });
                }

                using (var s = zip.CreateEntry("objects/object_horse/gHorseSkel").Open())
                {
                    s.Write(new byte[] { 9 });
                }
            }

            var output = Path.Combine(dir, "zz_coop_p05_m_0.o2r");
            var result = ModelRepack.Repack([input], output, 5);

            Assert.True(result.Ok, result.Error);
            Assert.Equal(2, result.Kept);
            Assert.Equal(1, result.StringRefs);
            Assert.Equal(1, result.HashRefs);

            using var repacked = ZipFile.OpenRead(output);
            var names = repacked.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
            Assert.Equal(
                ["p05objs/object_link_child/gLinkHumanEyesTex", "p05objs/object_link_child/gLinkHumanSkel"],
                names);

            using var stream = repacked.GetEntry("p05objs/object_link_child/gLinkHumanSkel")!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var rewritten = buffer.ToArray();

            const string expectedTex = "p05objs/object_link_child/gLinkHumanEyesTex";
            Assert.Equal(expectedTex, Encoding.ASCII.GetString(rewritten, 0, expectedTex.Length));

            var newHash = Crc64.OfPath(expectedTex);
            var tail = rewritten.AsSpan(expectedTex.Length + 1);
            Assert.Equal((uint)(newHash >> 32), BitConverter.ToUInt32(tail[..4]));
            Assert.Equal((uint)newHash, BitConverter.ToUInt32(tail.Slice(4, 4)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Repack_GivesEachPlayerADistinctNamespace()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var input = Path.Combine(dir, "model.o2r");
            using (var file = File.Create(input))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using var s = zip.CreateEntry("objects/object_link_child/gLinkHumanSkel").Open();
                s.Write(new byte[] { 1 });
            }

            var first = Path.Combine(dir, "one.o2r");
            var second = Path.Combine(dir, "two.o2r");
            Assert.True(ModelRepack.Repack([input], first, 1).Ok);
            Assert.True(ModelRepack.Repack([input], second, 2).Ok);

            using var a = ZipFile.OpenRead(first);
            using var b = ZipFile.OpenRead(second);
            Assert.Equal("p01objs/object_link_child/gLinkHumanSkel", a.Entries.Single().FullName);
            Assert.Equal("p02objs/object_link_child/gLinkHumanSkel", b.Entries.Single().FullName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteArchive(string dir, string name, params (string Path, byte[] Data)[] entries)
    {
        var archive = Path.Combine(dir, name);
        using var file = File.Create(archive);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (path, data) in entries)
        {
            using var s = zip.CreateEntry(path).Open();
            s.Write(data);
        }

        return archive;
    }

    private static byte[] ReadEntry(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void Repack_KeepsAltPrefixedReferencesResolvable()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string limb = "alt/objects/object_link_zora/gLinkZoraCollarLimb";
            const string dl = "alt/objects/object_link_zora/gLinkZoraCollarDL";
            var limbBody = new List<byte> { 0x2E };
            limbBody.AddRange(Encoding.ASCII.GetBytes(dl));
            limbBody.Add(0x2E);
            var input = WriteArchive(dir, "zora.o2r",
                (limb, limbBody.ToArray()),
                (dl, Encoding.ASCII.GetBytes("<DisplayList/>")),
                ("alt/objects/object_link_zora/gLinkZoraUnusedDL", [1]));

            var output = Path.Combine(dir, "out.o2r");
            Assert.True(ModelRepack.Repack([input], output, 3).Ok);

            using var zip = ZipFile.OpenRead(output);
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
            Assert.Equal(
                [
                    "alt/p03objs/object_link_zora/gLinkZoraCollarDL",
                    "p03objs/object_link_zora/gLinkZoraCollarDL",
                    "p03objs/object_link_zora/gLinkZoraCollarLimb",
                    "p03objs/object_link_zora/gLinkZoraUnusedDL",
                ],
                names);
            Assert.Contains("alt/p03objs/object_link_zora/gLinkZoraCollarDL",
                            Encoding.ASCII.GetString(ReadEntry(zip, "p03objs/object_link_zora/gLinkZoraCollarLimb")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Repack_RewritesHashesOfAltPathsEvenWhenTheEntryIsPlain()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string tex = "objects/object_link_child/gLinkHumanEyesTex";
            var hash = Crc64.OfPath("alt/" + tex);
            var body = new List<byte>();
            body.AddRange(BitConverter.GetBytes((uint)(hash >> 32)));
            body.AddRange(BitConverter.GetBytes((uint)hash));
            var input = WriteArchive(dir, "model.o2r",
                ("objects/object_link_child/gLinkHumanDL", body.ToArray()),
                (tex, [1, 2, 3]));

            var output = Path.Combine(dir, "out.o2r");
            Assert.True(ModelRepack.Repack([input], output, 4).Ok);

            using var zip = ZipFile.OpenRead(output);
            var rewritten = ReadEntry(zip, "p04objs/object_link_child/gLinkHumanDL");
            var expected = Crc64.OfPath("p04objs/object_link_child/gLinkHumanEyesTex");
            Assert.Equal((uint)(expected >> 32), BitConverter.ToUInt32(rewritten, 0));
            Assert.Equal((uint)expected, BitConverter.ToUInt32(rewritten, 4));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Repack_PrefersAltOverPlainLikeTheGameDoes()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string path = "object_link_boy/gLinkAdultTunicTex";
            var first = WriteArchive(dir, "a.o2r", ("alt/objects/" + path, [7]));
            var second = WriteArchive(dir, "b.o2r", ("objects/" + path, [1]));
            var output = Path.Combine(dir, "out.o2r");

            Assert.True(ModelRepack.Repack([first, second], output, 2).Ok);

            using var zip = ZipFile.OpenRead(output);
            Assert.Equal([7], ReadEntry(zip, "p02objs/" + path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ModelSyncTests
{
    private static readonly Seed TestSeed = new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "seed",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = 2,
        PlayerCount = 4,
    };

    private static readonly VerifiedClientIdentity TestClient = new(
        Room: "seed", SettingsHash: "settings", TeamId: "team", PlayerId: 2, PlayerCount: 4);

    [Fact]
    public void AnnounceLine_IsAcceptedByTheRelayAllowlist()
    {
        var line = ModelSyncProtocol.BuildAnnounce(
            TestSeed, new ModelAnnounce(2, Game.Oot, "link.o2r", "abc123", 4096));

        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
        var parsed = ModelSyncProtocol.ParseAnnounce(line);
        Assert.NotNull(parsed);
        Assert.Equal(("link.o2r", "abc123", 4096L), (parsed!.Name, parsed.Hash, parsed.Size));
    }

    [Fact]
    public void EmptySetAnnounce_RoundTripsAndPassesTheAllowlist()
    {
        var line = ModelSyncProtocol.BuildAnnounce(
            TestSeed, new ModelAnnounce(2, Game.Mm, "", new string('0', 64), 1, 0, 0));

        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(line, TestClient));
        var parsed = ModelSyncProtocol.ParseAnnounce(line);
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed!.Count);
    }

    [Fact]
    public void RequestAndChunkLines_AreAcceptedByTheRelayAllowlist()
    {
        var request = ModelSyncProtocol.BuildRequest(TestSeed, new ModelRequest(2, 3, "abc123"));
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(request, TestClient));
        Assert.Equal(3, ModelSyncProtocol.ParseRequest(request)!.TargetPlayer);

        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var chunk = ModelSyncProtocol.BuildChunk(TestSeed, new ModelChunk(2, "abc123", 0, 2, payload));
        Assert.True(SideChannelPolicy.IsRoomPacketAllowed(chunk, TestClient));
        Assert.Equal(payload, ModelSyncProtocol.ParseChunk(chunk)!.Data);
    }

    [Fact]
    public void Download_AcceptsChunksInAnyOrderAndAssemblesThemInSequence()
    {
        var download = new ModelDownload("abc123", 3);
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 2, 3, [5, 6])));
        Assert.False(download.Accept(new ModelChunk(1, "abc123", 2, 3, [5, 6])));
        Assert.False(download.Accept(new ModelChunk(1, "abc123", 3, 3, [7, 8])));
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 0, 3, [1, 2])));
        Assert.False(download.Complete);
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 1, 3, [3, 4])));

        Assert.True(download.Complete);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, download.Assemble());
    }

    [Fact]
    public void SizeCap_AppliesToTheWholeSetRatherThanEachArchive()
    {
        var half = ModelSyncPolicy.CapBytes(100) / 2;
        Assert.True(ModelSyncPolicy.WithinCap(half, 100));
        Assert.False(ModelSyncPolicy.WithinCap(half * 2 + 1, 100));
    }

    [Fact]
    public void RemoteModelGroup_TotalsEveryAnnouncedArchive()
    {
        var group = new RemoteModelGroup();
        group.Announces.Add(new ModelAnnounce(3, Game.Oot, "a.o2r", "h1", 60L * 1024 * 1024));
        group.Announces.Add(new ModelAnnounce(3, Game.Oot, "b.o2r", "h2", 60L * 1024 * 1024));

        Assert.Equal(120L * 1024 * 1024, group.TotalBytes);
        Assert.False(ModelSyncPolicy.WithinCap(group.TotalBytes, ModelSyncPolicy.DefaultMaxMegabytes));
    }
}

public class ModelPortTests
{
    private static string MakeArchive(string dir, params (string Path, byte[] Data)[] entries)
    {
        var archive = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".o2r");
        using var file = File.Create(archive);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (path, data) in entries)
        {
            using var s = zip.CreateEntry(path).Open();
            s.Write(data);
        }

        return archive;
    }

    [Fact]
    public void PortDirectory_RenamesPathsStringsAndHashes()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string skel = "objects/object_link_boy/gLinkAdultSkel";
            const string tex = "objects/object_link_boy/gLinkAdultTunicTex";
            var body = new List<byte>();
            body.AddRange(Encoding.ASCII.GetBytes(tex));
            body.Add(0);
            var hash = Crc64.OfPath(tex);
            body.AddRange(BitConverter.GetBytes((uint)(hash >> 32)));
            body.AddRange(BitConverter.GetBytes((uint)hash));

            var input = MakeArchive(dir,
                (skel, body.ToArray()),
                (tex, [1, 2, 3]),
                ("objects/object_link_child/gLinkChildSkel", [9]));

            var output = Path.Combine(dir, "port.o2r");
            var result = ModelRepack.PortDirectory([input], output, "object_link_boy", "ot_obj_link_boy");

            Assert.True(result.Ok, result.Error);
            Assert.Equal(2, result.Kept);

            using var ported = ZipFile.OpenRead(output);
            var names = ported.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
            Assert.Equal(
                ["objects/ot_obj_link_boy/gLinkAdultSkel", "objects/ot_obj_link_boy/gLinkAdultTunicTex"],
                names);

            using var stream = ported.GetEntry("objects/ot_obj_link_boy/gLinkAdultSkel")!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var rewritten = buffer.ToArray();

            const string expectedTex = "objects/ot_obj_link_boy/gLinkAdultTunicTex";
            Assert.Equal(expectedTex, Encoding.ASCII.GetString(rewritten, 0, expectedTex.Length));
            var newHash = Crc64.OfPath(expectedTex);
            var tail = rewritten.AsSpan(expectedTex.Length + 1);
            Assert.Equal((uint)(newHash >> 32), BitConverter.ToUInt32(tail[..4]));
            Assert.Equal((uint)newHash, BitConverter.ToUInt32(tail.Slice(4, 4)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PortDirectory_FlattensAltEntriesToCanonicalAndPrefersAltOnCollision()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var input = MakeArchive(dir,
                ("alt/objects/object_link_boy/gLinkAdultTunicTex", [7, 7]),
                ("objects/object_link_boy/gLinkAdultTunicTex", [1]));
            var output = Path.Combine(dir, "port.o2r");

            var result = ModelRepack.PortDirectory([input], output, "object_link_boy", "ot_obj_link_boy");

            Assert.True(result.Ok, result.Error);
            using var ported = ZipFile.OpenRead(output);
            var entry = ported.Entries.Single();
            Assert.Equal("objects/ot_obj_link_boy/gLinkAdultTunicTex", entry.FullName);
            Assert.Equal(2, entry.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PortDirectory_KeepsAltPrefixedReferencesResolvable()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string dl = "alt/objects/object_link_boy/gLinkAdultCollarDL";
            var input = MakeArchive(dir,
                ("alt/objects/object_link_boy/gLinkAdultCollarLimb", Encoding.ASCII.GetBytes(dl)),
                (dl, [1]));
            var output = Path.Combine(dir, "port.o2r");

            Assert.True(ModelRepack.PortDirectory([input], output, "object_link_boy", "ot_obj_link_boy").Ok);

            using var ported = ZipFile.OpenRead(output);
            Assert.NotNull(ported.GetEntry("objects/ot_obj_link_boy/gLinkAdultCollarDL"));
            Assert.NotNull(ported.GetEntry("alt/objects/ot_obj_link_boy/gLinkAdultCollarDL"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PortDirectory_TreatsNoMatchingEntriesAsSuccessAndRemovesStaleOutput()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var input = MakeArchive(dir, ("objects/object_link_child/gLinkChildSkel", [1]));
            var output = Path.Combine(dir, "port.o2r");
            File.WriteAllBytes(output, [0]);

            var result = ModelRepack.PortDirectory([input], output, "object_link_boy", "ot_obj_link_boy");

            Assert.True(result.Ok, result.Error);
            Assert.Equal(0, result.Kept);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PortDirectory_RefusesLengthChangingRenames()
    {
        var result = ModelRepack.PortDirectory([], "out.o2r", "object_link_boy", "objects_link_boy2");
        Assert.False(result.Ok);
    }
}
