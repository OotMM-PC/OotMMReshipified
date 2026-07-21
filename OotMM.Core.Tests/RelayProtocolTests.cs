using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class RelayProtocolTests
{
    private static Seed MakeSeed() => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "protocol-test",
        SettingsHash = "settings",
        TeamId = "team",
        PlayerId = 1,
        PlayerCount = 2,
        SessionEpoch = 4,
    };

    [Fact]
    public void BuildHello_MatchesWireContract()
    {
        var hello = RelayProtocol.BuildHello(MakeSeed(), "cafe", "p1", 0);
        Assert.Equal(
            "{\"type\":\"OOTMM_HELLO\",\"protocolVersion\":4,\"format\":\"ootmm.pc.seed.v2\","
            + "\"generatorVersion\":\"31.1\",\"seedId\":\"protocol-test\","
            + "\"settingsHash\":\"settings\",\"teamId\":\"team\",\"playerId\":1,"
            + "\"playerCount\":2,\"saveId\":\"cafe\",\"logicalSlot\":0,"
            + "\"sessionEpoch\":\"4\",\"clientName\":\"p1\"}",
            hello);
    }

    [Fact]
    public void FrameGate_AllowsOnlyHandshakeFrames()
    {
        Assert.True(RelayProtocol.FrameAllowedBeforeProtocolReady(RelayProtocol.Pong));
        Assert.True(RelayProtocol.FrameAllowedBeforeProtocolReady(RelayProtocol.Ready));
        Assert.True(RelayProtocol.FrameAllowedBeforeProtocolReady(RelayProtocol.ProtocolError));
        Assert.False(RelayProtocol.FrameAllowedBeforeProtocolReady(RelayProtocol.SessionMeta));
        Assert.False(RelayProtocol.FrameAllowedBeforeProtocolReady(RelayProtocol.GiveItem));
        Assert.False(RelayProtocol.FrameAllowedBeforeProtocolReady(""));
    }

    [Fact]
    public void ExtractStringField_RequiresColonAndQuotes()
    {
        Assert.Equal("oot", RelayProtocol.ExtractStringField("{\"g\":\"oot\"}", "g"));
        Assert.Equal("oot", RelayProtocol.ExtractStringField("{\"g\" \t: \"oot\"}", "g"));
        Assert.Equal("", RelayProtocol.ExtractStringField("{\"g\":5}", "g"));
        Assert.Equal("", RelayProtocol.ExtractStringField("{\"g\" \"oot\"}", "g"));
        Assert.Equal("", RelayProtocol.ExtractStringField("{\"g\":\"unterminated", "g"));
        Assert.Equal("", RelayProtocol.ExtractStringField("{}", "g"));
    }

    [Fact]
    public void ExtractIntField_DigitsOnlyWithFallback()
    {
        Assert.Equal(4, RelayProtocol.ExtractIntField("{\"protocolVersion\":4}", "protocolVersion"));
        Assert.Equal(7, RelayProtocol.ExtractIntField("{\"x\":\"nope\"}", "x", 7));
        Assert.Equal(7, RelayProtocol.ExtractIntField("{}", "x", 7));
        Assert.Equal(7, RelayProtocol.ExtractIntField("{\"x\":99999999999999999999}", "x", 7));
    }

    [Fact]
    public void ExtractU64Field_QuotedOrBare_OverflowFallsBack()
    {
        Assert.Equal(4ul, RelayProtocol.ExtractU64Field("{\"sessionEpoch\":\"4\"}", "sessionEpoch"));
        Assert.Equal(4ul, RelayProtocol.ExtractU64Field("{\"sessionEpoch\":4}", "sessionEpoch"));
        Assert.Equal(ulong.MaxValue, RelayProtocol.ExtractU64Field(
            "{\"x\":\"18446744073709551615\"}", "x"));
        Assert.Equal(9ul, RelayProtocol.ExtractU64Field("{\"x\":\"18446744073709551616\"}", "x", 9));
        Assert.Equal(9ul, RelayProtocol.ExtractU64Field("{\"x\":\"4}", "x", 9)); // unclosed quote
        Assert.Equal(9ul, RelayProtocol.ExtractU64Field("{\"x\":\"\"}", "x", 9));
    }
}
