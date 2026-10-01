using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class SharedResourceSnapshotTests
{
    private const string Initial = """{"version":1,"saveId":"world-save","slot":0,"revision":1,"source":"oot","values":{"bombchu":20},"applied":{"SHARED_BOMBCHU_BAG":1}}""";
    private const string Consumed = """{"version":1,"saveId":"world-save","slot":0,"revision":2,"source":"mm","values":{"bombchu":19},"applied":{"SHARED_BOMBCHU_BAG":1}}""";

    private static SharedResourceSnapshot Parse(string text)
    {
        Assert.True(SharedResourceSnapshot.TryParse(text, "world-save", 0, out var value, out var error), error);
        return Assert.IsType<SharedResourceSnapshot>(value);
    }

    [Fact]
    public void ConsumedAmmoWinsOverOlderSaveWithSameRewardCount()
    {
        var initial = Parse(Initial);
        var consumed = Parse(Consumed);
        foreach (var candidates in new[] { new[] { initial, consumed }, new[] { consumed, initial, consumed } })
        {
            Assert.True(SharedResourceSnapshot.TrySelect(candidates, "world-save", 0, out var latest, out _));
            Assert.Equal(19u, latest!.Values["bombchu"]);
            Assert.Equal(1u, latest.Applied["SHARED_BOMBCHU_BAG"]);
            Assert.Equal(consumed.Serialize(), Parse(latest.Serialize()).Serialize());
        }
    }

    [Fact]
    public void EqualRevisionWithDifferentStateFailsInsteadOfRefilling()
    {
        var consumed = Parse(Consumed);
        var refill = Parse(Consumed.Replace("\"bombchu\":19", "\"bombchu\":20"));
        Assert.False(SharedResourceSnapshot.TrySelect([consumed, refill], "world-save", 0, out var result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void ConflictDetectionDoesNotDependOnEnumerationOrder()
    {
        var first = Parse(Initial);
        var conflict = Parse(Initial.Replace("\"bombchu\":20", "\"bombchu\":19"));
        var newest = Parse(Consumed);
        foreach (var candidates in new[] { new[] { first, conflict, newest }, new[] { newest, first, conflict } })
        {
            Assert.False(SharedResourceSnapshot.TrySelect(candidates, "world-save", 0, out var result, out _));
            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData("other-save", 0)]
    [InlineData("world-save", 1)]
    public void SaveAndSlotBoundariesRejectCrossovers(string saveId, uint slot)
    {
        Assert.False(SharedResourceSnapshot.TryParse(Consumed, saveId, slot, out _, out _));
        Assert.False(SharedResourceSnapshot.TrySelect([Parse(Consumed)], saveId, slot, out _, out _));
    }

    [Theory]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"revision\":1", "\"revision\":0")]
    [InlineData("\"revision\":1", "\"revision\":9007199254740992")]
    [InlineData("\"revision\":1", "\"revision\":\"1\"")]
    [InlineData("\"bombchu\":20", "\"bombchu\":-1")]
    [InlineData("\"bombchu\":20", "\"bombchu\":65536")]
    [InlineData("\"bombchu\":20", "\"bombchu\":20,\"bombchu\":19")]
    [InlineData("\"slot\":0", "\"slot\":0,\"slot\":0")]
    [InlineData("\"SHARED_BOMBCHU_BAG\":1", "\"OOT_BOMBCHU_BAG\":1")]
    [InlineData("\"SHARED_BOMBCHU_BAG\":1", "\"SHARED_BOMBCHU_BAG\":1000001")]
    [InlineData("\"source\":\"oot\"", "\"source\":\"shared\"")]
    public void RejectsMalformedOrIncompatibleSnapshots(string before, string after)
    {
        Assert.False(SharedResourceSnapshot.TryParse(Initial.Replace(before, after), "world-save", 0, out var result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void LegacySavesWithoutSnapshotsDoNotInventAmmo()
    {
        Assert.True(SharedResourceSnapshot.TrySelect([], "world-save", 0, out var result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void BootCarriesSnapshotOnlyForItsBoundSaveAndSlot()
    {
        var boot = new BootConfig { SaveId = "world-save", LogicalSlot = 0, SharedResources = Parse(Consumed) };
        var parsed = BootConfig.Parse(boot.Serialize());
        Assert.NotNull(parsed);
        Assert.Equal(19u, parsed.SharedResources!.Values["bombchu"]);
        Assert.Null(BootConfig.Parse(boot.Serialize().Replace("\"logicalSlot\":0", "\"logicalSlot\":1")));
        Assert.Null(BootConfig.Parse(boot.Serialize().Replace(",\"logicalSlot\":0", "")));
        Assert.Null(BootConfig.Parse(boot.Serialize().Insert(1, "\"logicalSlot\":0,")));
        Assert.Null(BootConfig.Parse(boot.Serialize().Replace("\"bombchu\":19", "\"bombchu\":19,\"bombchu\":20")));
        boot.LogicalSlot = 1;
        Assert.Throws<InvalidOperationException>(() => boot.Serialize());
    }
}
