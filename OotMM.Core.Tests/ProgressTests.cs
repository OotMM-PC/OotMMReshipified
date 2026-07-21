using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class ProgressTests
{
    private static ItemPlacement Placement(
        Game checkGame, string checkId, string itemId, ushort owner, bool major = false) =>
        new(new CheckRef(checkGame, checkId, checkId, [], null, "", null),
            new ItemRef(itemId.StartsWith("MM_") ? Game.Mm : Game.Oot, itemId, itemId + " name"),
            null, owner, owner, major);

    private static ProgressReport BuildReport(
        out List<(ItemPlacement, bool)> checks, int bridgeCount = 2)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "S",
            PlayerId = 1,
            PlayerCount = 2,
            SpecialConds = new Dictionary<string, Seed.SpecialCond>
            {
                ["BRIDGE"] = new(bridgeCount, new Dictionary<string, bool> { ["stones"] = true }),
            },
        };

        checks =
        [
            (Placement(Game.Oot, "KEY1", "OOT_SMALL_KEY_FOREST", owner: 1), true),
            (Placement(Game.Oot, "KEY2", "OOT_SMALL_KEY_FOREST", owner: 1), false),
            (Placement(Game.Oot, "BK", "OOT_BOSS_KEY_FOREST", owner: 1), false),
            (Placement(Game.Oot, "MAP", "OOT_MAP_FOREST", owner: 1), true),
            (Placement(Game.Oot, "STONE1", "OOT_STONE_EMERALD", owner: 1), true),
            (Placement(Game.Oot, "STONE2", "OOT_STONE_RUBY", owner: 1), true),
            (Placement(Game.Oot, "SONG", "SHARED_SONG_EPONA", owner: 1), false),
            (Placement(Game.Oot, "HOOK", "OOT_HOOKSHOT", owner: 1, major: true), true),
            (Placement(Game.Mm, "SOUL", "MM_SOUL_BOSS_ODOLWA", owner: 1), false),
            (Placement(Game.Mm, "FAIRY", "MM_STRAY_FAIRY_WF", owner: 1), true),
            // Owned by the other player: contributes to check counts, never to pools.
            (Placement(Game.Mm, "THEIRS", "MM_MASK_BUNNY", owner: 2), true),
        ];

        var counts = new Dictionary<string, int>();
        foreach (var (placement, collected) in checks)
        {
            if (collected && placement.OwnerPlayer == 1)
            {
                counts[placement.Item.Id] = counts.GetValueOrDefault(placement.Item.Id) + 1;
            }
        }

        var localChecks = checks;
        return ProgressReport.Build(seed, localChecks, counts);
    }

    [Fact]
    public void ChecksSummary_CountsPerGame()
    {
        var report = BuildReport(out _);
        Assert.Equal(new ChecksSummary(7, 11, 5, 8, 2, 3), report.Checks);
    }

    [Fact]
    public void Dungeons_DeriveFromPoolAndHave()
    {
        var report = BuildReport(out _);
        var forest = Assert.Single(report.DungeonRows, d => d.Name == "Forest Temple");
        Assert.Equal(1, forest.KeysHave);
        Assert.Equal(2, forest.KeysTotal);
        Assert.True(forest.HasBossKey);
        Assert.False(forest.BossKey);
        Assert.True(forest.Map);

        var woodfall = Assert.Single(report.DungeonRows, d => d.Name == "Woodfall Temple");
        Assert.True(woodfall.HasFairies);
        Assert.Equal(1, woodfall.FairiesHave);

        Assert.DoesNotContain(report.DungeonRows, d => d.Name == "Fire Temple"); // empty pool hidden
    }

    [Fact]
    public void SharedSongs_AppearInBothGames()
    {
        var report = BuildReport(out _);
        Assert.Contains(report.Songs(Game.Oot), s => s.Id == "SHARED_SONG_EPONA");
        Assert.Contains(report.Songs(Game.Mm), s => s.Id == "SHARED_SONG_EPONA");
        Assert.False(report.Songs(Game.Oot)[0].Owned);
    }

    [Fact]
    public void KeyItemsSoulsAndGoals()
    {
        var report = BuildReport(out _);

        var hookshot = Assert.Single(report.KeyItems(Game.Oot));
        Assert.Equal("OOT_HOOKSHOT name", hookshot.Name);
        Assert.True(hookshot.Owned);
        Assert.Empty(report.KeyItems(Game.Mm)); // other player's mask never pools locally

        Assert.Single(report.Souls(Game.Mm, SoulSub.Boss));
        Assert.Empty(report.Souls(Game.Oot, SoulSub.Boss));

        var bridge = Assert.Single(report.Goals);
        Assert.Equal("BRIDGE", bridge.Name);
        Assert.Equal(2, bridge.Owned); // emerald + ruby collected
        Assert.True(bridge.Active);
        Assert.True(bridge.Satisfied);

        Assert.False(BuildReport(out _, bridgeCount: 3).Goals[0].Satisfied);
    }
}
