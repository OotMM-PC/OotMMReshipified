using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class MmRecoveryRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("007bff80")]
    public void PreservesMissingEmptyAndArbitraryPrimaryBytes(string? primary)
    {
        var request = new MmRecoveryRequest("{\"native\":\"save\"}", primary);
        var boot = new BootConfig
        {
            SaveId = new string('1', 32), LogicalSlot = 2, BootGame = Game.Mm, MmRecovery = request,
        };
        Assert.Equal(request, BootConfig.Parse(boot.Serialize())!.MmRecovery);
    }

    [Theory]
    [InlineData("{\"version\":\"1\",\"backup\":\"{}\",\"primaryHex\":null}")]
    [InlineData("{\"version\":2,\"backup\":\"{}\",\"primaryHex\":null}")]
    [InlineData("{\"version\":1,\"backup\":\"{}\",\"primaryHex\":\"0\"}")]
    [InlineData("{\"version\":1,\"backup\":\"{}\",\"primaryHex\":\"GG\"}")]
    [InlineData("{\"version\":1,\"backup\":\"{}\",\"primaryHex\":null,\"extra\":1}")]
    [InlineData("{\"version\":1,\"backup\":\"{}\",\"backup\":\"[]\",\"primaryHex\":null}")]
    public void RejectsAmbiguousUnsupportedOrMalformedRequests(string value)
    {
        Assert.False(MmRecoveryRequest.TryParse(value, out var request));
        Assert.Null(request);
    }

    [Fact]
    public void RecoveryCannotBeSentToOotOrUnboundSave()
    {
        var boot = new BootConfig { MmRecovery = new MmRecoveryRequest("{}", null) };
        Assert.Throws<InvalidOperationException>(() => boot.Serialize());
        Assert.Null(BootConfig.Parse("{\"bootGame\":\"oot\",\"logicalSlot\":0,\"saveId\":\"11111111111111111111111111111111\",\"mmRecovery\":{\"version\":1,\"backup\":\"{}\",\"primaryHex\":null}}"));
    }
}
