using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class WarpDestinationTests
{
    private static Seed CreateSeed() => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "warp-destination-test",
        EntrancesSong = [0x8000d890u, 0x05f4u],
        EntrancesOwl = [0x68b0u, 0x80000600u],
    };

    [Theory]
    [InlineData(Game.Mm, 0xd890u, true, false)]
    [InlineData(Game.Oot, 0xd890u, false, false)]
    [InlineData(Game.Oot, 0x05f4u, true, false)]
    [InlineData(Game.Mm, 0x05f4u, false, false)]
    [InlineData(Game.Mm, 0x68b0u, false, true)]
    [InlineData(Game.Oot, 0x68b0u, false, false)]
    [InlineData(Game.Oot, 0x0600u, false, true)]
    [InlineData(Game.Mm, 0x0600u, false, false)]
    [InlineData(Game.Mm, 0x8000d890u, false, false)]
    [InlineData(Game.Oot, 0x800068b0u, false, false)]
    [InlineData((Game)99, 0xd890u, false, false)]
    [InlineData(Game.Mm, 0x1234u, false, false)]
    public void MatchesResolvedTableWithoutCrossingGamesOrWarpFamilies(
        Game game, uint nativeId, bool song, bool owl)
    {
        var seed = CreateSeed();
        Assert.Equal(song, seed.IsSongDestination(game, nativeId));
        Assert.Equal(owl, seed.IsOwlDestination(game, nativeId));
        Assert.False(seed.IsSpawnDestination(game, nativeId));
    }
}
