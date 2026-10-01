using System.Text.Json.Nodes;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class NativeSaveCopyPayloadTests
{
    private const string SourceId = "11111111111111111111111111111111";
    private const string TargetId = "22222222222222222222222222222222";

    private static JsonObject Snapshot(int revision, int ammo) => new()
    {
        ["version"] = 1, ["saveId"] = SourceId, ["slot"] = 0, ["revision"] = revision,
        ["source"] = "mm", ["values"] = new JsonObject { ["bombchu"] = ammo },
        ["applied"] = new JsonObject { ["SHARED_BOMBCHU_BAG"] = 1 },
    };

    private static JsonObject Section(int revision, int ammo) => new()
    {
        ["save"] = new JsonObject { ["ammo"] = ammo, ["quest"] = "preserve" },
        ["ootmmApplied"] = new JsonObject { ["SHARED_BOMBCHU_BAG"] = 1 },
        ["ootmmSharedResources"] = Snapshot(revision, ammo),
    };

    [Fact]
    public void RebindsBothMmEnvelopesWithoutFlatteningCycleAndOwlState()
    {
        var source = new JsonObject
        {
            ["newCycleSave"] = Section(4, 0), ["owlSave"] = Section(9, 8),
            ["ootmmChecks"] = new JsonArray("check-a"), ["futureField"] = new JsonArray(5, 7),
        };
        Assert.True(NativeSaveCopyPayload.TryCreate(Game.Mm, source.ToJsonString(), SourceId, 0,
            TargetId, 1, out var copied, out var error), error);
        var actual = JsonNode.Parse(copied)!;
        foreach (var name in new[] { "newCycleSave", "owlSave" })
        {
            Assert.Equal(TargetId, actual[name]!["ootmmSharedResources"]!["saveId"]!.GetValue<string>());
            Assert.Equal(1, actual[name]!["ootmmSharedResources"]!["slot"]!.GetValue<int>());
            actual[name]!["ootmmSharedResources"]!["saveId"] = SourceId;
            actual[name]!["ootmmSharedResources"]!["slot"] = 0;
        }
        Assert.True(JsonNode.DeepEquals(source, actual));
    }

    [Fact]
    public void OotRetainsNativeInventoryAppliedCountsAndUnknownSections()
    {
        var source = new JsonObject
        {
            ["sections"] = new JsonObject { ["base"] = new JsonObject { ["data"] = new JsonObject { ["ammo"] = 8 } } },
            ["ootmmSharedResources"] = Snapshot(9, 8), ["ootmmApplied"] = new JsonObject { ["SHARED_BOMBCHU_BAG"] = 1 },
        };
        Assert.True(NativeSaveCopyPayload.TryCreate(Game.Oot, source.ToJsonString(), SourceId, 0,
            TargetId, 2, out var copied, out var error), error);
        var actual = JsonNode.Parse(copied)!;
        Assert.Equal(2, actual["ootmmSharedResources"]!["slot"]!.GetValue<int>());
        actual["ootmmSharedResources"]!["saveId"] = SourceId;
        actual["ootmmSharedResources"]!["slot"] = 0;
        Assert.True(JsonNode.DeepEquals(source, actual));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"owlSave\":null}")]
    [InlineData("{\"owlSave\":{\"save\":{\"a\":1,\"a\":2}}}")]
    [InlineData("{\"owlSave\":{\"save\":{},\"ootmmSharedResources\":null}}")]
    public void RejectsIncompleteOrAmbiguousNativePayloads(string source)
    {
        Assert.False(NativeSaveCopyPayload.TryCreate(Game.Mm, source, SourceId, 0, TargetId, 1, out var copy, out _));
        Assert.Equal("", copy);
    }

    [Fact]
    public void WrongScopeInEitherEnvelopeRejectsWholeCopy()
    {
        var source = new JsonObject { ["newCycleSave"] = Section(1, 0), ["owlSave"] = Section(2, 8) };
        source["owlSave"]!["ootmmSharedResources"]!["slot"] = 2;
        Assert.False(NativeSaveCopyPayload.TryCreate(Game.Mm, source.ToJsonString(), SourceId, 0,
            TargetId, 1, out var copy, out _));
        Assert.Equal("", copy);
    }
}
