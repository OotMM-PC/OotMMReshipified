using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class BossWarpTests
{
    public static TheoryData<uint, Game, uint> GeneratorDestinations => new()
    {
        { 0x00000209, Game.Oot, 0x00000457 },
        { 0x00000242, Game.Oot, 0x00000242 },
        { 0x00000221, Game.Oot, 0x00000221 },
        { 0x00000215, Game.Oot, 0x00000600 },
        { 0x0000024a, Game.Oot, 0x000004f6 },
        { 0x0000021d, Game.Oot, 0x00000604 },
        { 0x00000205, Game.Oot, 0x00000568 },
        { 0x000001e1, Game.Oot, 0x000001e1 },
        { 0x80008610, Game.Mm, 0x00008600 },
        { 0x8000b210, Game.Mm, 0x00009a70 },
        { 0x80006a70, Game.Mm, 0x00006a70 },
        { 0x8000ac10, Game.Mm, 0x00002070 },
    };

    [Theory]
    [MemberData(nameof(GeneratorDestinations))]
    public void AcceptsGeneratorDestination(uint warp, Game game, uint nativeId)
    {
        Assert.True(BossWarp.IsDestinationAllowed([warp], game, nativeId));
    }

    [Fact]
    public void AcceptsRaisedWoodfallDestination()
    {
        Assert.True(BossWarp.IsDestinationAllowed([0x80008610], Game.Mm, 0x3010));
    }

    [Fact]
    public void AcceptsArbitraryResolvedDungeonDestination()
    {
        Assert.True(BossWarp.IsDestinationAllowed([0x80002050], Game.Mm, 0x2050));
    }

    [Fact]
    public void RejectsDestinationInWrongGame()
    {
        Assert.False(BossWarp.IsDestinationAllowed([0x80002050], Game.Oot, 0x2050));
    }

    [Theory]
    [InlineData(Game.Oot, 0x80002050u)]
    [InlineData(Game.Mm, 0x80002050u)]
    [InlineData((Game)99, 0x2050u)]
    public void RejectsInvalidGameOrEncodedId(Game game, uint nativeId)
    {
        Assert.False(BossWarp.IsDestinationAllowed([0x80002050], game, nativeId));
    }

    [Fact]
    public void RejectsUnlistedDestination()
    {
        Assert.False(BossWarp.IsDestinationAllowed([0x80008610], Game.Mm, 0x9a70));
        Assert.False(BossWarp.IsDestinationAllowed([], Game.Oot, 0x457));
    }
}
