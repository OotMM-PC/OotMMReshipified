using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class ContainerContentsTests
{
    private static Seed SeedWith(Dictionary<string, string>? strings = null, Dictionary<string, bool>? bools = null) =>
        new()
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 1,
            PlayerCount = 1,
            StringSettings = strings ?? new Dictionary<string, string>(),
            BoolSettings = bools ?? new Dictionary<string, bool>(),
        };

    private static ItemPlacement Placement(Game checkGame, string itemId, string? cloakId = null) =>
        new(new CheckRef(checkGame, "CHECK", "CHECK", [], null, "", null),
            new ItemRef(Game.Oot, itemId, itemId),
            cloakId is null ? null : new ItemRef(Game.Oot, cloakId, cloakId),
            1, 1, false);

    [Theory]
    [InlineData("OOT_OCARINA")]
    [InlineData("MM_OCARINA")]
    [InlineData("OOT_SWORD")]
    [InlineData("MM_SWORD")]
    [InlineData("OOT_SCALE")]
    [InlineData("MM_STRENGTH")]
    [InlineData("OOT_SHIELD")]
    [InlineData("MM_CLOCK")]
    [InlineData("SHARED_OCARINA")]
    [InlineData("SHARED_TRIFORCE")]
    [InlineData("SHARED_MASK_SKULL")]
    public void ProgressiveAndSharedItemsAreMajor(string itemId)
    {
        Assert.Equal(ContentClass.Major, ContainerContents.Classify(SeedWith(), Placement(Game.Mm, itemId)));
    }

    [Theory]
    [InlineData("OOT_SMALL_KEY_FOREST", ContentClass.Key)]
    [InlineData("OOT_BOSS_KEY_FOREST", ContentClass.BossKey)]
    [InlineData("OOT_GS_TOKEN", ContentClass.Spider)]
    [InlineData("MM_STRAY_FAIRY_WF", ContentClass.Fairy)]
    [InlineData("MM_HEART_PIECE", ContentClass.Heart)]
    [InlineData("MM_SOUL_BOSS_ODOLWA", ContentClass.Soul)]
    [InlineData("OOT_MAP_FOREST", ContentClass.MapCompass)]
    [InlineData("OOT_RUPEE_GREEN", ContentClass.Normal)]
    [InlineData("OOT_SEEDS_30", ContentClass.Normal)]
    public void ItemsFollowTheGamesContainerClasses(string itemId, ContentClass expected)
    {
        Assert.Equal(expected, ContainerContents.Classify(SeedWith(), Placement(Game.Oot, itemId)));
    }

    [Fact]
    public void CloaksAndSeedSettingsDecideWhatIsShown()
    {
        Assert.Equal(ContentClass.Major,
            ContainerContents.Classify(SeedWith(), Placement(Game.Oot, "OOT_TRAP_ICE", "OOT_OCARINA")));
        Assert.Equal(ContentClass.Normal,
            ContainerContents.Classify(SeedWith(), Placement(Game.Oot, "OOT_TRAP_ICE")));
        Assert.Equal(ContentClass.Major,
            ContainerContents.Classify(SeedWith(bools: new() { ["cloakTraps"] = true }), Placement(Game.Oot, "OOT_TRAP_ICE")));
        Assert.Equal(ContentClass.Normal,
            ContainerContents.Classify(SeedWith(bools: new() { ["csmcHearts"] = false }), Placement(Game.Mm, "MM_HEART_PIECE")));
        Assert.Equal(ContentClass.Major,
            ContainerContents.Classify(SeedWith(new() { ["bombchuBehaviorOot"] = "bagFirst" }), Placement(Game.Oot, "OOT_BOMBCHU_10")));
        Assert.Equal(ContentClass.Normal,
            ContainerContents.Classify(SeedWith(), Placement(Game.Oot, "OOT_BOMBCHU_10")));
    }

    [Fact]
    public void RevealFollowsTheSeedRuleAndEachGamesStone()
    {
        static Func<string, bool> Owning(params string[] items) => items.Contains;

        var always = SeedWith(new() { ["csmc"] = "always" });
        Assert.True(ContainerContents.IsRevealed(always, Game.Oot, Owning()));

        var never = SeedWith(new() { ["csmc"] = "never" });
        Assert.False(ContainerContents.IsRevealed(never, Game.Mm, Owning("MM_STONE_OF_AGONY", "OOT_STONE_OF_AGONY")));

        var split = SeedWith(new() { ["csmc"] = "agony" }, new() { ["stoneAgonyMm"] = true });
        Assert.True(ContainerContents.IsRevealed(split, Game.Mm, Owning("MM_STONE_OF_AGONY")));
        Assert.False(ContainerContents.IsRevealed(split, Game.Oot, Owning("MM_STONE_OF_AGONY")));
        Assert.False(ContainerContents.IsRevealed(split, Game.Mm, Owning("OOT_STONE_OF_AGONY")));

        var ootOnly = SeedWith(new() { ["csmc"] = "agony" });
        Assert.True(ContainerContents.IsRevealed(ootOnly, Game.Mm, Owning("OOT_STONE_OF_AGONY")));

        var shared = SeedWith(new() { ["csmc"] = "agony" }, new() { ["sharedStoneAgony"] = true });
        Assert.True(ContainerContents.IsRevealed(shared, Game.Oot, Owning("SHARED_STONE_OF_AGONY")));
        Assert.True(ContainerContents.IsRevealed(shared, Game.Mm, Owning("SHARED_STONE_OF_AGONY")));
        Assert.False(ContainerContents.IsRevealed(shared, Game.Mm, Owning("MM_STONE_OF_AGONY")));
    }
}
