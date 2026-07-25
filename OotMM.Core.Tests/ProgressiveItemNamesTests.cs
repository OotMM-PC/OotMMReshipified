using OotMM.Core;

namespace OotMM.Core.Tests;

public class ProgressiveItemNamesTests
{
    [Fact]
    public void Resolve_UsesTheRecipientTierAndSeedSettings()
    {
        var seed = MakeSeed(
            boolSettings: new Dictionary<string, bool> { ["bronzeScale"] = true });
        var scale = new ItemRef(Game.Oot, "OOT_SCALE", "Progressive Scale (OoT)");

        Assert.Equal("Bronze Scale",
            ProgressiveItemNames.Resolve(seed, Game.Oot, scale, new Dictionary<string, uint>()));
        Assert.Equal("Golden Scale",
            ProgressiveItemNames.Resolve(seed, Game.Oot, scale,
                new Dictionary<string, uint>
                {
                    ["OOT_SCALE"] = 1,
                    ["SHARED_SCALE"] = 1,
                }));
    }

    [Fact]
    public void Resolve_ProgressiveClockStartsAfterTheInitiallyOwnedHalfDay()
    {
        var seed = MakeSeed(
            stringSettings: new Dictionary<string, string>
            {
                ["progressiveClocks"] = "ascending",
            });
        var clock = new ItemRef(Game.Mm, "MM_CLOCK", "Progressive Clock");

        Assert.Equal("Clock (Night 1)",
            ProgressiveItemNames.Resolve(seed, Game.Mm, clock, new Dictionary<string, uint>()));
        Assert.Equal("Clock (Day 2)",
            ProgressiveItemNames.Resolve(seed, Game.Mm, clock,
                new Dictionary<string, uint> { ["MM_CLOCK"] = 1 }));
    }

    [Fact]
    public void Resolve_WorksForAnotherPlayersRecipientState()
    {
        var playerTwoSeed = MakeSeed(playerId: 2);
        var ocarina = new ItemRef(Game.Oot, "OOT_OCARINA", "Progressive Ocarina (OoT)");

        Assert.Equal("Fairy Ocarina",
            ProgressiveItemNames.Resolve(
                playerTwoSeed, Game.Oot, ocarina, new Dictionary<string, uint>()));
        Assert.Equal("Ocarina of Time",
            ProgressiveItemNames.Resolve(
                playerTwoSeed, Game.Oot, ocarina,
                new Dictionary<string, uint> { ["OOT_OCARINA"] = 1 }));
    }

    private static Seed MakeSeed(
        ushort playerId = 1,
        IReadOnlyDictionary<string, bool>? boolSettings = null,
        IReadOnlyDictionary<string, string>? stringSettings = null) =>
        new()
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "progressive-names",
            PlayerId = playerId,
            PlayerCount = 2,
            BoolSettings = boolSettings ?? new Dictionary<string, bool>(),
            StringSettings = stringSettings ?? new Dictionary<string, string>(),
        };
}
