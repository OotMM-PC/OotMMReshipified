using System.Text.Json;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class GameIpcProtocolTests
{
    [Fact]
    public void TryParseHello_ReadsValidMessage()
    {
        const string json =
            """{"type":"hello","protocolMajor":1,"protocolMinor":0,"token":"secret","game":"mm","pid":42,"build":"test"}""";

        Assert.True(GameIpcProtocol.TryParseHello(json, out var hello, out var error), error);
        Assert.NotNull(hello);
        Assert.Equal(Game.Mm, hello!.Game);
        Assert.Equal(42, hello.Pid);
        Assert.Equal("secret", hello.Token);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"type":"hello","game":"bad"}""")]
    [InlineData("invalid")]
    public void TryParseHello_RejectsInvalidMessage(string json)
    {
        Assert.False(GameIpcProtocol.TryParseHello(json, out var hello, out var error));
        Assert.Null(hello);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void BuildSettingsSnapshot_UsesSharedSettingNames()
    {
        var settings = new RosterMeta(true, true, false, 175, true, "syncing", 3);

        using var document = JsonDocument.Parse(GameIpcProtocol.BuildSettingsSnapshot(7, settings));
        var root = document.RootElement;
        Assert.Equal(GameIpcProtocol.MessageType.SettingsSnapshot, root.GetProperty("type").GetString());
        Assert.Equal(7, root.GetProperty("revision").GetInt64());
        Assert.Equal(175, root.GetProperty("settings").GetProperty("speedPercent").GetInt32());
        Assert.True(root.GetProperty("settings").GetProperty("speedSmooth").GetBoolean());
    }

    [Fact]
    public void ReadType_RejectsNonObjectJson() =>
        Assert.Equal("", GameIpcProtocol.ReadType("[]"));

    [Fact]
    public void TryParseCrossGameTransition_ReadsNativeDestination()
    {
        const string json =
            """{"type":"crossGameTransition","fromGame":"oot","toGame":"mm","toEntrance":"MM_CLOCK_TOWN","toNativeId":55296,"ootAge":1}""";

        Assert.True(
            GameIpcProtocol.TryParseCrossGameTransition(json, out var transition, out var error),
            error);
        Assert.NotNull(transition);
        Assert.Equal(Game.Oot, transition!.FromGame);
        Assert.Equal(Game.Mm, transition.ToGame);
        Assert.Equal("MM_CLOCK_TOWN", transition.ToEntrance);
        Assert.Equal((uint)55296, transition.ToNativeId);
        Assert.Equal((uint)1, transition.OotAge);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"type":"crossGameTransition","fromGame":"oot","toGame":"oot","toEntrance":""}""")]
    [InlineData("""{"type":"crossGameTransition","fromGame":"oot","toGame":"oot","toEntrance":"OOT_MARKET"}""")]
    [InlineData("invalid")]
    public void TryParseCrossGameTransition_RejectsMalformedMessages(string json)
    {
        Assert.False(
            GameIpcProtocol.TryParseCrossGameTransition(json, out var transition, out var error));
        Assert.Null(transition);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void BuildCrossGameTransition_CreatesRecoverableAnchorPacket()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "seed",
            SettingsHash = "settings",
            TeamId = "team",
            PlayerId = 2,
        };
        var transition = new CrossGameTransition(
            Game.Mm, Game.Oot, "OOT_TEMPLE_SHADOW", 55, 1);

        var packet = AnchorPackets.BuildCrossGameTransition(transition, seed);

        Assert.Equal(transition, AnchorPackets.ParseCrossGameTransition(packet));
        Assert.True(AnchorPackets.MatchesSeedIdentity(packet, seed));
    }
}
