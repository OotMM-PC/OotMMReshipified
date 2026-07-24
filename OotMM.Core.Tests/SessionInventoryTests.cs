using OotMM.Core;

namespace OotMM.Core.Tests;

public class SessionInventoryTests
{
    [Fact]
    public void Inventory_IsDerivedFromStartingItemsOwnedChecksAndRemoteReceipts()
    {
        var directory = CreateDirectory();
        try
        {
            var seed = MakeSeed();
            var inventory = new SessionInventory(seed, Path.Combine(directory, "debug-items.json"));

            Assert.Equal(2u, inventory.Items["OOT_COIN_RED"]);

            inventory.Reconcile(
                [Identity.CheckKey(Game.Oot, "OOT_LOCAL"), Identity.CheckKey(Game.Mm, "MM_FOREIGN")],
                new Dictionary<string, int> { ["MM_SOUL_GOHT"] = 1 });

            Assert.Equal(3u, inventory.Items["OOT_COIN_RED"]);
            Assert.Equal(1u, inventory.Items["MM_SOUL_GOHT"]);
            Assert.DoesNotContain("MM_BUTTON_A", inventory.Items);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DebugEdit_AdjustsTheSameInventoryAndPersistsOnlyTheAdjustment()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "debug-items.json");
            var seed = MakeSeed();
            var inventory = new SessionInventory(seed, path);

            Assert.False(File.Exists(path));
            Assert.True(inventory.TrySetDebugValue("OOT_COIN_RED", 5));
            Assert.Equal(5u, inventory.Items["OOT_COIN_RED"]);

            inventory.Reconcile(
                [Identity.CheckKey(Game.Oot, "OOT_LOCAL")],
                new Dictionary<string, int>());
            Assert.Equal(6u, inventory.Items["OOT_COIN_RED"]);

            var restored = new SessionInventory(seed, path);
            restored.Reconcile(
                [Identity.CheckKey(Game.Oot, "OOT_LOCAL")],
                new Dictionary<string, int>());
            Assert.Equal(6u, restored.Items["OOT_COIN_RED"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DebugEdit_RejectsUnknownOrInvalidItems()
    {
        var directory = CreateDirectory();
        try
        {
            var inventory = new SessionInventory(
                MakeSeed(), Path.Combine(directory, "debug-items.json"));

            Assert.False(inventory.TrySetDebugValue("MM_UNKNOWN", 1));
            Assert.False(inventory.TrySetDebugValue("../BAD", 1));
            Assert.False(inventory.TrySetDebugValue("OOT_COIN_RED", SessionInventory.MaxValue + 1));
            Assert.Equal(2u, inventory.Items["OOT_COIN_RED"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StaleDebugAdjustment_DoesNotIntroduceAnItemAbsentFromTheSeed()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "debug-items.json");
            File.WriteAllText(path,
                """{"format":"ootmm.pc.debug-items.v1","adjustments":{"MM_UNKNOWN":1}}""");

            var inventory = new SessionInventory(MakeSeed(), path);

            Assert.True(inventory.IsValid);
            Assert.DoesNotContain("MM_UNKNOWN", inventory.Items);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Seed MakeSeed() =>
        new()
        {
            Format = Seed.CurrentFormat,
            GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "inventory-test",
            PlayerId = 1,
            StartingItems =
            [
                new StartingItem(new ItemRef(Game.Oot, "OOT_COIN_RED", "Red Coin"), 2),
            ],
            Placements =
            [
                new ItemPlacement(
                    new CheckRef(Game.Oot, "OOT_LOCAL", "Local", [], null, "", null),
                    new ItemRef(Game.Oot, "OOT_COIN_RED", "Red Coin"),
                    null, 1, 1, false),
                new ItemPlacement(
                    new CheckRef(Game.Mm, "MM_FOREIGN", "Foreign", [], null, "", null),
                    new ItemRef(Game.Mm, "MM_BUTTON_A", "A Button"),
                    null, 2, 1, false),
            ],
        };

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-inventory-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        return directory;
    }
}
