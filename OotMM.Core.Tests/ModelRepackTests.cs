using System.IO.Compression;
using System.Text;
using OotMM.Core.Models;
using OotMM.Core.Relay;
using Xunit;

namespace OotMM.Core.Tests;

public class ModelRepackTests
{
    /// <summary>Values produced by libultraship's own CRC64 for these paths. If the port drifts,
    /// every rewritten reference points at nothing and models silently fail to resolve.</summary>
    [Theory]
    [InlineData("objects/object_link_child/gLinkHumanSkel", 6914884263858874222UL)]
    [InlineData("p01objs/object_link_child/gLinkHumanSkel", 13930404445129331195UL)]
    [InlineData("objects/object_link_boy/gLinkAdultSkel", 17343821888806444020UL)]
    [InlineData("p0aobjs/object_link_boy/gLinkAdultSkel", 7759082645766003366UL)]
    [InlineData("", 18446744073709551615UL)]
    public void Crc64_MatchesLibultraship(string path, ulong expected) =>
        Assert.Equal(expected, Crc64.OfPath(path));

    /// <summary>The namespace prefix must be exactly as long as the directory it replaces, or
    /// references cannot be patched in place inside binary resources.</summary>
    [Fact]
    public void NamespacePrefix_IsTheSameLengthAsTheDirectoryItReplaces() =>
        Assert.Equal("objects/".Length, (ModelRepack.PlayerPrefix(7) + "objs/").Length);

    [Theory]
    [InlineData("objects/object_link_child/gLinkHumanSkel", "p03objs/object_link_child/gLinkHumanSkel")]
    [InlineData("alt/objects/object_link_boy/gLinkAdultSkel", "p03objs/object_link_boy/gLinkAdultSkel")]
    public void NamespacedPath_UnifiesAltAndCanonicalIntoOneNamespace(string input, string expected) =>
        Assert.Equal(expected, ModelRepack.NamespacedPath(input, ModelRepack.PlayerPrefix(3)));

    /// <summary>Anything outside the player's model is dropped: keeping it would override the
    /// asset for every player, not just the puppet wearing this model.</summary>
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

            // The skeleton references the texture the way a real resource does: once as a path
            // string, once as the CRC64 of that path.
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
            Assert.Equal(2, result.Kept); // the horse entry is outside the model namespace
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

    /// <summary>Two players wearing the same model must not collide in the resource cache.</summary>
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

    /// <summary>Chunks assemble strictly in order, so a gap fails loudly instead of producing an
    /// archive with a hole in it.</summary>
    [Fact]
    public void Download_RejectsOutOfOrderChunksAndAssemblesInOrder()
    {
        var download = new ModelDownload("abc123", 3);
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 0, 3, [1, 2])));
        Assert.False(download.Accept(new ModelChunk(1, "abc123", 2, 3, [5, 6])));
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 1, 3, [3, 4])));
        Assert.False(download.Complete);
        Assert.True(download.Accept(new ModelChunk(1, "abc123", 2, 3, [5, 6])));

        Assert.True(download.Complete);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, download.Assemble());
    }

    [Fact]
    public void SizeCap_AppliesToTheWholeSetRatherThanEachArchive()
    {
        var half = ModelSyncPolicy.CapBytes(100) / 2;
        Assert.True(ModelSyncPolicy.WithinCap(half, 100));
        // Two archives that each fit still exceed the cap together.
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
