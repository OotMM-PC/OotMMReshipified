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
}
