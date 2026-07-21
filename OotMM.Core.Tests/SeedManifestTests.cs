using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class SeedManifestTests
{
    private static Seed MakeSeed(bool trapsLink = false) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "protocol-test",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = 1,
        PlayerCount = 2,
        SessionEpoch = 4,
        BoolSettings = trapsLink
            ? new Dictionary<string, bool> { ["trapsLink"] = true }
            : new Dictionary<string, bool>(),
        Placements =
        [
            new ItemPlacement(
                new CheckRef(Game.Oot, "CHECK_A", "Check A", [], null, "", null),
                new ItemRef(Game.Oot, "OOT_BOW", "Bow"), null, 1, 1, false),
            new ItemPlacement(
                new CheckRef(Game.Oot, "CHECK_REMOTE_MM", "", [], null, "", null),
                new ItemRef(Game.Mm, "MM_HOOKSHOT", "Hookshot"), null, 2, 1, false),
        ],
    };

    [Fact]
    public void Build_MatchesWireContract()
    {
        var seed = MakeSeed();
        Assert.Equal(
            "{\"type\":\"OOTMM_SEED_MANIFEST\",\"seedId\":\"protocol-test\","
            + "\"settingsHash\":\"settings\",\"generatorVersion\":\"31.1\",\"teamId\":\"team\","
            + "\"playerId\":1,\"playerCount\":2,\"protocolVersion\":3,\"sessionEpoch\":\"4\","
            + $"\"manifestHash\":\"{Identity.SeedManifestHash(seed)}\","
            + "\"format\":\"ootmm.pc.seed.v2\",\"placements\":["
            + "{\"checkGame\":\"oot\",\"checkId\":\"CHECK_A\",\"itemGame\":\"oot\","
            + "\"itemId\":\"OOT_BOW\",\"itemName\":\"Bow\",\"ownerPlayer\":1},"
            + "{\"checkGame\":\"oot\",\"checkId\":\"CHECK_REMOTE_MM\",\"itemGame\":\"mm\","
            + "\"itemId\":\"MM_HOOKSHOT\",\"itemName\":\"Hookshot\",\"ownerPlayer\":2}]}",
            SeedManifest.Build(seed));
    }

    [Fact]
    public void Parse_RoundTripsAndRebuildsIdentically()
    {
        var seed = MakeSeed(trapsLink: true);
        var packet = SeedManifest.Build(seed);
        Assert.Contains(",\"trapsLink\":true", packet);

        var parsed = SeedManifest.Parse(packet);
        Assert.NotNull(parsed);
        Assert.Equal(4ul, parsed.SessionEpoch);
        Assert.Equal(Identity.SeedManifestHash(seed), parsed.ManifestHash);
        Assert.Equal(2, parsed.Seed.Placements.Count);
        Assert.True(parsed.Seed.GetBoolSetting("trapsLink"));
        // Placement source is the sender; owner comes from the packet.
        Assert.Equal(1, (int)parsed.Seed.Placements[1].SourcePlayer);
        Assert.Equal(2, (int)parsed.Seed.Placements[1].OwnerPlayer);
        // A parsed manifest re-serializes byte-identically (the relay compares raw bytes).
        Assert.Equal(packet, SeedManifest.Build(parsed.Seed));
    }

    [Fact]
    public void Parse_RejectsTamperedOrMalformedPackets()
    {
        var packet = SeedManifest.Build(MakeSeed());
        Assert.Null(SeedManifest.Parse(packet.Replace("\"itemId\":\"OOT_BOW\"", "\"itemId\":\"OOT_SWORD\"")));
        Assert.Null(SeedManifest.Parse(packet.Replace("\"protocolVersion\":3", "\"protocolVersion\":4")));
        Assert.Null(SeedManifest.Parse(packet.Replace("\"sessionEpoch\":\"4\"", "\"sessionEpoch\":\"0\"")));
        Assert.Null(SeedManifest.Parse(packet.Replace("CHECK_REMOTE_MM", "CHECK_A"))); // duplicate check
        Assert.Null(SeedManifest.Parse(packet.Replace("\"ownerPlayer\":2", "\"ownerPlayer\":3")));
        Assert.Null(SeedManifest.Parse(packet.Replace("\"type\":\"OOTMM_SEED_MANIFEST\"", "\"type\":\"OTHER\"")));
        Assert.Null(SeedManifest.Parse("not json"));
    }
}
