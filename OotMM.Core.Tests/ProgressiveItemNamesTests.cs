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

    [Fact]
    public void ResolveItem_UsesTheSelectedIdAndRecipientProgression()
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "resolved-rewards",
            Placements =
            [
                new(new CheckRef(Game.Mm, "MM_TEST", "Test", [], null, "npc", null),
                    new ItemRef(Game.Oot, "OOT_OCARINA", "Progressive Ocarina"), null, 1, 1, true),
            ],
            StartingItems = [new(new ItemRef(Game.Mm, "MM_DEED_LAND", "Land Title Deed"), 1)],
        };
        var inventory = new Dictionary<string, uint> { ["OOT_OCARINA"] = 1 };

        Assert.Equal(new ItemRef(Game.Oot, "OOT_OCARINA", "Ocarina of Time"),
            ProgressiveItemNames.ResolveItem(seed, "OOT_OCARINA", inventory));
        Assert.Equal(new ItemRef(Game.Mm, "MM_DEED_LAND", "Land Title Deed"),
            ProgressiveItemNames.ResolveItem(seed, "MM_DEED_LAND", inventory));
        Assert.Equal("Progressive Ocarina", seed.Placements[0].Item.Name);
        Assert.Equal(1u, inventory["OOT_OCARINA"]);
    }

    [Theory]
    [InlineData("MM_UNKNOWN")]
    [InlineData("")]
    [InlineData("mm_deed_land")]
    public void ResolveItem_RejectsUnknownIdsWithoutInventingAnItem(string itemId)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "resolved-rewards",
            StartingItems = [new(new ItemRef(Game.Mm, "MM_DEED_LAND", "Land Title Deed"), 1)],
        };

        Assert.Null(ProgressiveItemNames.ResolveItem(seed, itemId, new Dictionary<string, uint>()));
    }

    [Fact]
    public void ResolveCheck_PreservesCrossGameRecipientAndUsesTheSameItemResolution()
    {
        var placement = new ItemPlacement(
            new CheckRef(Game.Mm, "MM_NPC", "NPC", ["MM_NPC_ALIAS"], null, "npc", null),
            new ItemRef(Game.Oot, "OOT_OCARINA", "Progressive Ocarina"), null, 2, 1, true);
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "check-reward-resolution",
            PlayerCount = 2,
            Placements = [placement],
        };
        var recipientInventory = new Dictionary<string, uint> { ["OOT_OCARINA"] = 1 };
        var resolved = ProgressiveItemNames.ResolveCheck(seed, Game.Mm, "MM_NPC", recipientInventory);

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.OwnerPlayer);
        Assert.Equal(1, resolved.SourcePlayer);
        Assert.Equal(placement.Check, resolved.Check);
        Assert.Equal(ProgressiveItemNames.ResolveItem(seed, "OOT_OCARINA", recipientInventory), resolved.Item);
        Assert.Equal("Ocarina of Time", resolved.Item.Name);
        Assert.Equal(resolved,
            ProgressiveItemNames.ResolveCheck(seed, Game.Mm, "MM_NPC_ALIAS", recipientInventory));
        Assert.Equal("Progressive Ocarina", placement.Item.Name);
        Assert.Equal(1u, recipientInventory["OOT_OCARINA"]);
    }

    [Theory]
    [InlineData(Game.Oot, "MM_NPC")]
    [InlineData(Game.Mm, "MM_MISSING")]
    public void ResolveCheck_RejectsWrongGameOrUnknownCheck(Game game, string checkId)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "check-reward-resolution",
            Placements =
            [
                new(new CheckRef(Game.Mm, "MM_NPC", "NPC", [], null, "npc", null),
                    new ItemRef(Game.Mm, "MM_DEED_LAND", "Land Title Deed"), null, 1, 1, true),
            ],
        };

        Assert.Null(ProgressiveItemNames.ResolveCheck(seed, game, checkId, new Dictionary<string, uint>()));
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
