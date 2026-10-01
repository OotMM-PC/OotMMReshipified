using System.Text.Json.Nodes;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class NativeSaveRequestTests
{
    private const string Valid = """{"type":"nativeSaveRequest","version":1,"requestId":3,"operation":"copy","saveId":"source-save","activeSlot":0,"sourceSlot":0,"targetSlot":1}""";

    [Fact]
    public void CopyRequiresCurrentIdentityAndNewRequest()
    {
        Assert.True(NativeSaveRequest.TryParse(Valid, out var request, out var error), error);
        Assert.Equal(NativeSaveOperation.Copy, request!.Operation);
        Assert.True(request.MatchesActiveSave("source-save", 0, 2));
        Assert.False(request.MatchesActiveSave("different-save", 0, 2));
        Assert.False(request.MatchesActiveSave("source-save", 1, 2));
        Assert.False(request.MatchesActiveSave("source-save", null, 2));
        Assert.False(request.MatchesActiveSave("source-save", 0, 3));
        Assert.False(request.MatchesActiveSave("source-save", 0, 4));
    }

    [Fact]
    public void CopySourceCanDifferFromTheActiveSlot()
    {
        var node = JsonNode.Parse(Valid)!;
        node["sourceSlot"] = 2;
        Assert.True(NativeSaveRequest.TryParse(node.ToJsonString(), out var request, out var error), error);
        Assert.True(request!.MatchesActiveSave("source-save", 0, 2));
        Assert.Equal(2u, request.SourceSlot);
        Assert.Equal(1u, request.TargetSlot);
    }

    [Theory]
    [InlineData("activeSlot", "3")]
    [InlineData("activeSlot", "null")]
    [InlineData("requestId", "0")]
    [InlineData("requestId", "-1")]
    [InlineData("requestId", "9007199254740992")]
    [InlineData("requestId", "1.5")]
    [InlineData("requestId", "\"3\"")]
    [InlineData("version", "2")]
    [InlineData("sourceSlot", "3")]
    [InlineData("sourceSlot", "4294967296")]
    [InlineData("targetSlot", "-1")]
    [InlineData("targetSlot", "0")]
    [InlineData("operation", "\"delete\"")]
    [InlineData("saveId", "\"\"")]
    [InlineData("saveId", "null")]
    [InlineData("saveId", "\"bad\\nidentity\"")]
    public void RejectsInvalidFields(string name, string value)
    {
        var node = JsonNode.Parse(Valid)!;
        node[name] = JsonNode.Parse(value);
        Assert.False(NativeSaveRequest.TryParse(node.ToJsonString(), out var request, out _));
        Assert.Null(request);
    }

    [Fact]
    public void RejectsAmbiguousAndIncompleteRequests()
    {
        Assert.False(NativeSaveRequest.TryParse(Valid.Insert(1, "\"targetSlot\":2,"), out _, out _));
        Assert.False(NativeSaveRequest.TryParse("[]", out _, out _));
        Assert.False(NativeSaveRequest.TryParse("{", out _, out _));
        var node = JsonNode.Parse(Valid)!;
        node.AsObject().Remove("saveId");
        Assert.False(NativeSaveRequest.TryParse(node.ToJsonString(), out _, out _));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    public void SelectionSupportsAllSlotsAndReselectingCurrentSlot(uint slot)
    {
        var node = JsonNode.Parse(Valid)!;
        node["operation"] = "select";
        node["targetSlot"] = slot;
        node["requestId"] = NativeSaveRequest.MaximumRequestId;
        Assert.True(NativeSaveRequest.TryParse(node.ToJsonString(), out var request, out var error), error);
        Assert.Equal(slot, request!.TargetSlot);
        Assert.Equal(NativeSaveOperation.Select, request.Operation);
    }
}
