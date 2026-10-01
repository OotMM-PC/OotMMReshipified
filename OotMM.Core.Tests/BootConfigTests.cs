using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class BootConfigTests
{
    [Fact]
    public void FileSelectBootIsExplicitAndRejectsAmbiguousValues()
    {
        Assert.False(BootConfig.Parse("{}")!.OpenFileSelect);
        Assert.True(BootConfig.Parse(new BootConfig { OpenFileSelect = true }.Serialize())!.OpenFileSelect);
        Assert.Null(BootConfig.Parse("{\"openFileSelect\":1}"));
        Assert.Null(BootConfig.Parse("{\"openFileSelect\":\"true\"}"));
    }

    [Fact]
    public void Serialize_OmitsEmptyAndDefaultFields()
    {
        var config = new BootConfig
        {
            SaveId = "save1",
            SeedPath = @"C:\seed.json",
            StatePath = @"C:\state.json",
            InboxPath = @"C:\in.jsonl",
            OutboxPath = @"C:\out.jsonl",
        };

        Assert.Equal(
            """{"saveId":"save1","seedPath":"C:\\seed.json","statePath":"C:\\state.json","inboxPath":"C:\\in.jsonl","outboxPath":"C:\\out.jsonl","allowDebugMenus":false,"bootGame":"oot"}""",
            config.Serialize());
    }

    [Fact]
    public void Serialize_EmitsOptionalsInCppOrder()
    {
        var config = new BootConfig
        {
            SaveId = "s",
            MmSaveDir = @"D:\saves",
            AllowDebugMenus = true,
            MultiworldProtocol = 4,
            BootGame = Game.Mm,
            BootEntrance = 586,
            OotAge = 1,
            LogicalSlot = 2,
            ParentWindow = 18446744073709551615,
        };

        Assert.Equal(
            """{"saveId":"s","seedPath":"","statePath":"","inboxPath":"","outboxPath":"","mmSaveDir":"D:\\saves","allowDebugMenus":true,"multiworldProtocol":4,"bootGame":"mm","bootEntrance":586,"ootAge":1,"logicalSlot":2,"parentWindow":"18446744073709551615"}""",
            config.Serialize());
    }

    [Fact]
    public void SerializeParse_RoundTrips()
    {
        var config = new BootConfig
        {
            SaveId = "id",
            SeedPath = "seed",
            PresenceInPath = "pin",
            MultiworldProtocol = 4,
            BootGame = Game.Mm,
            BootEntrance = 3446,
            LogicalSlot = 1,
            ParentWindow = 123456789012345,
        };

        var parsed = BootConfig.Parse(config.Serialize());
        Assert.NotNull(parsed);
        Assert.Equal(config.Serialize(), parsed!.Serialize());
    }

    [Fact]
    public void Parse_ToleratesMissingFieldsAndNumericParentWindow()
    {
        var config = BootConfig.Parse("""{"seedPath":"x","parentWindow":42,"future":1}""");
        Assert.NotNull(config);
        Assert.Equal("", config!.SaveId);
        Assert.Equal("x", config.SeedPath);
        Assert.Equal(3, config.MultiworldProtocol);
        Assert.Equal(42ul, config.ParentWindow);
    }

    [Theory]
    [InlineData("""{"logicalSlot":3}""")]
    [InlineData("""{"logicalSlot":-1}""")]
    [InlineData("""{"logicalSlot":1.5}""")]
    [InlineData("""{"logicalSlot":"1"}""")]
    [InlineData("[]")]
    [InlineData("nope")]
    public void Parse_RejectsInvalidLogicalSlotOrRoot(string json) =>
        Assert.Null(BootConfig.Parse(json));
}

public class SessionOwnershipTests
{
    private static Seed MakeSeed() => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "SEED",
        SettingsHash = "HASH",
        TeamId = "team",
        PlayerId = 2,
        SessionEpoch = 7,
    };

    [Fact]
    public void Marker_HasCanonicalShapeAndValidatesByteExact()
    {
        var seed = MakeSeed();
        var marker = SessionOwnership.BuildMarker(seed, "key_v4");

        Assert.Equal(
            "{\"format\":\"ootmm.session-owner.v2\",\"sessionKey\":\"key_v4\",\"seedId\":\"SEED\","
            + "\"settingsHash\":\"HASH\",\"teamId\":\"team\",\"playerId\":2,\"sessionEpoch\":\"7\","
            + $"\"manifestHash\":\"{Identity.SeedManifestHash(seed)}\"}}",
            marker);

        Assert.True(SessionOwnership.ValidateMarker(marker, seed, "key_v4"));
        Assert.False(SessionOwnership.ValidateMarker(marker, seed, "other"));
        Assert.False(SessionOwnership.ValidateMarker(marker + " ", seed, "key_v4"));
    }

    private static string StateWithSlot(string slot) =>
        $$$"""{"sessionValues":{"ootmm.activeSlot":"{{{slot}}}"}}""";

    [Fact]
    public void ParseRuntimeLogicalSlot_ReadsExactIdentity()
    {
        Assert.Equal(1u, SessionOwnership.ParseRuntimeLogicalSlot(StateWithSlot("1"), out _));

        Assert.Null(SessionOwnership.ParseRuntimeLogicalSlot(StateWithSlot("3"), out var range));
        Assert.Contains("invalid", range);
        Assert.Null(SessionOwnership.ParseRuntimeLogicalSlot("{}", out var missing));
        Assert.Contains("sessionValues", missing);
        Assert.Null(SessionOwnership.ParseRuntimeLogicalSlot("junk", out var bad));
        Assert.Contains("cannot parse", bad);
    }

    [Fact]
    public void ResolveLogicalSlot_CoversTheStateMachine()
    {
        var unverified = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(false, null, [null, null, null], false));
        Assert.False(unverified.IsSafe);

        var baseMatch = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, StateWithSlot("1"), [null, StateWithSlot("1"), null], false));
        Assert.True(baseMatch.IsSafe);
        Assert.Equal(1u, baseMatch.LogicalSlot);
        Assert.False(baseMatch.ProvenNew);

        var sidecarMismatch = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, StateWithSlot("1"), [null, StateWithSlot("2"), null], false));
        Assert.False(sidecarMismatch.IsSafe);

        var recovered = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, null, [null, null, StateWithSlot("2")], false));
        Assert.True(recovered.IsSafe);
        Assert.Equal(2u, recovered.LogicalSlot);

        var ambiguous = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, null, [StateWithSlot("0"), StateWithSlot("1"), null], false));
        Assert.False(ambiguous.IsSafe);

        var orphanedNativeSave = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, null, [null, null, null], true));
        Assert.False(orphanedNativeSave.IsSafe);

        var fresh = SessionOwnership.ResolveLogicalSlotIdentity(
            new LogicalSlotEvidence(true, null, [null, null, null], false));
        Assert.True(fresh.IsSafe);
        Assert.Equal(0u, fresh.LogicalSlot);
        Assert.True(fresh.ProvenNew);
    }
}
