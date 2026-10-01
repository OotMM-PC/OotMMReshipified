using System.Text.Json;
using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class NativeMmSaveValidationTests
{
    [Fact]
    public void PairRecoveryRequestUsesTheExactBoundPrimaryAndIdentity()
    {
        var primary = Path.Combine(Path.GetTempPath(), "pair-qualification", "file3.json");
        using var document = JsonDocument.Parse(NativeMmSaveValidation.SerializePairRecoveryRequest(primary, new string('a', 32), 2));
        var root = document.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal(Path.GetFullPath(primary), root.GetProperty("primary").GetString());
        Assert.Equal(new string('a', 32), root.GetProperty("saveId").GetString());
        Assert.Equal(2, root.GetProperty("slot").GetInt32());
    }

    [Theory]
    [InlineData("file1.json", "", 0u)]
    [InlineData("file1.json", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 3u)]
    [InlineData("file2.json", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0u)]
    [InlineData("other.json", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0u)]
    public void RejectsPairRecoveryForAnotherPathOrIdentity(string primary, string saveId, uint slot)
    {
        Assert.Throws<ArgumentException>(() => NativeMmSaveValidation.SerializePairRecoveryRequest(primary, saveId, slot));
    }

    [Fact]
    public async Task PairRecoveryDoesNotStartANativeProcessWithoutATransaction()
    {
        var primary = Path.Combine(Path.GetTempPath(), "pair-absent-" + Guid.NewGuid(), "file1.json");
        Assert.Null(await NativeMmSaveValidation.RecoverPairAsync("missing.exe", primary, new string('a', 32), 0,
            CancellationToken.None));
    }

    [Fact]
    public void RequestPreservesExactSelectedContentsAndIdentity()
    {
        const string contents = " {\"name\":\"é\\n\"}\n";
        using var document = JsonDocument.Parse(NativeMmSaveValidation.SerializeRequest(contents, new string('1', 32), 2));
        var root = document.RootElement;
        Assert.Equal(contents, root.GetProperty("contents").GetString());
        Assert.Equal(2, root.GetProperty("slot").GetInt32());
        Assert.Equal(new string('1', 32), root.GetProperty("saveId").GetString());
    }

    [Theory]
    [InlineData("", 0u)]
    [InlineData("11111111111111111111111111111111", 3u)]
    public void RejectsInvalidRequestIdentity(string saveId, uint slot)
    {
        Assert.Throws<ArgumentException>(() => NativeMmSaveValidation.SerializeRequest("{}", saveId, slot));
    }

    [Theory]
    [InlineData("{\"version\":1,\"valid\":true,\"reason\":\"\"}", 1)]
    [InlineData("{\"version\":1,\"valid\":false,\"reason\":\"bad save\"}", 0)]
    [InlineData("{\"version\":1,\"valid\":true,\"reason\":\"unexpected\"}", 0)]
    [InlineData("{\"version\":1,\"valid\":false,\"reason\":\"\"}", 1)]
    [InlineData("{\"version\":2,\"valid\":true,\"reason\":\"\"}", 0)]
    [InlineData("{\"version\":1,\"valid\":true,\"valid\":false,\"reason\":\"\"}", 0)]
    [InlineData("{\"version\":1,\"valid\":\"true\",\"reason\":\"\"}", 0)]
    [InlineData("{", 0)]
    public void RejectsAmbiguousMalformedOrContradictoryReplies(string output, int code)
    {
        Assert.Equal("MM save validation returned an invalid result.", NativeMmSaveValidation.ReadResult(output, code));
    }

    [Fact]
    public void AcceptsSuccessAndPreservesUsefulNativeFailure()
    {
        Assert.Null(NativeMmSaveValidation.ReadResult("{\"version\":1,\"valid\":true,\"reason\":\"\"}", 0));
        Assert.Equal("MM save validation failed: Invalid file magic.", NativeMmSaveValidation.ReadResult(
            "{\"version\":1,\"valid\":false,\"reason\":\"Invalid file magic.\"}", 1));
        Assert.NotNull(NativeMmSaveValidation.ReadResult(new string(' ', 16385), 0));
    }
}
