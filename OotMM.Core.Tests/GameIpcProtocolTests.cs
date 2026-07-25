using System.Text.Json;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class GameIpcProtocolTests
{
    [Fact]
    public void InventoryDebugSet_ParsesValidatedItemAndValue()
    {
        Assert.True(GameIpcProtocol.TryParseInventoryDebugSet(
            """{"type":"inventoryDebugSet","itemId":"OOT_COIN_RED","value":4}""",
            out var update, out var error));
        Assert.Equal("", error);
        Assert.Equal(new InventoryDebugSet("OOT_COIN_RED", 4), update);

        Assert.False(GameIpcProtocol.TryParseInventoryDebugSet(
            """{"type":"inventoryDebugSet","itemId":"../bad","value":4}""",
            out _, out _));
    }

    [Fact]
    public void InventoryMessages_UseRevisionedItemMaps()
    {
        using var snapshot = JsonDocument.Parse(GameIpcProtocol.BuildInventorySnapshot(
            4, new Dictionary<string, uint> { ["OOT_COIN_RED"] = 2 }));
        Assert.Equal(
            GameIpcProtocol.MessageType.InventorySnapshot,
            snapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal(4ul, snapshot.RootElement.GetProperty("revision").GetUInt64());
        Assert.Equal(2u, snapshot.RootElement.GetProperty("items").GetProperty("OOT_COIN_RED").GetUInt32());

        using var delta = JsonDocument.Parse(
            GameIpcProtocol.BuildInventoryDelta(5, "OOT_COIN_RED", 3));
        Assert.Equal(GameIpcProtocol.MessageType.InventoryDelta, delta.RootElement.GetProperty("type").GetString());
        Assert.Equal(5ul, delta.RootElement.GetProperty("revision").GetUInt64());
        Assert.Equal(3u, delta.RootElement.GetProperty("items").GetProperty("OOT_COIN_RED").GetUInt32());
    }

    [Fact]
    public void DebugPresentItemRoundTrips()
    {
        const string json =
            """{"type":"debugPresentItem","itemId":"MM_HAMMER","recipientPlayer":2}""";

        Assert.True(GameIpcProtocol.TryParseDebugPresentItem(
            json, out var request, out var error), error);
        Assert.Equal("MM_HAMMER", request!.ItemId);
        Assert.Equal((ushort)2, request.RecipientPlayer);
    }

    [Theory]
    [InlineData("""{"type":"debugPresentItem","itemId":"MM_HAMMER"}""")]
    [InlineData("""{"type":"debugPresentItem","itemId":"MM_HAMMER","recipientPlayer":0}""")]
    public void DebugPresentItemRejectsInvalidRecipient(string json)
    {
        Assert.False(GameIpcProtocol.TryParseDebugPresentItem(
            json, out var request, out var error));
        Assert.Null(request);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void PresentItemIncludesRoutingMetadata()
    {
        var json = GameIpcProtocol.BuildPresentItem(
            new ItemRef(Game.Mm, "MM_HAMMER", "Megaton Hammer (MM)"),
            Game.Mm,
            2,
            3,
            debug: true,
            viewerPlayer: 2);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("presentItem", root.GetProperty("type").GetString());
        Assert.Equal("MM_HAMMER", root.GetProperty("itemId").GetString());
        Assert.Equal("mm", root.GetProperty("itemGame").GetString());
        Assert.Equal("mm", root.GetProperty("destinationGame").GetString());
        Assert.Equal(2, root.GetProperty("sourcePlayer").GetInt32());
        Assert.Equal(3, root.GetProperty("recipientPlayer").GetInt32());
        Assert.Equal(2, root.GetProperty("viewerPlayer").GetInt32());
        Assert.True(root.GetProperty("debug").GetBoolean());
    }

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
