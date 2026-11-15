using System.Reflection;
using OotMM.Core;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public sealed class SessionCoordinatorTests
{
    [Theory]
    [InlineData(0u, "Fairy Ocarina")]
    [InlineData(1u, "Ocarina of Time")]
    public void CollectionLogUsesTheDurableResolvedTierWithoutLoggingRetries(uint prior, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-log-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var coordinator = new SessionCoordinator(RewardSession(directory), startingGame: Game.Mm);
            var inventory = (SessionInventory)typeof(SessionCoordinator)
                .GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            Assert.True(inventory.TrySetDebugValue("OOT_OCARINA", prior));
            var messages = new List<string>();
            coordinator.EventLogged += messages.Add;

            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD_ALIAS", true);
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);

            Assert.Equal($"[MM] Reward: found {expected}", Assert.Single(messages));
            Assert.Equal(expected, Rewards(coordinator)["mm:MM_REWARD"].Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CheckRewardRetryAndReopenPreserveTheOriginallyResolvedTier()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            using (var coordinator = new SessionCoordinator(session, startingGame: Game.Mm))
            {
                Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);
                Assert.Single(coordinator.CompletedChecks);
                var inventory = (SessionInventory)typeof(SessionCoordinator)
                    .GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
                Assert.True(inventory.TrySetDebugValue("OOT_OCARINA", 2));
                Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);
                Assert.Single(coordinator.CompletedChecks);
                Assert.Equal("Fairy Ocarina", Rewards(coordinator)["mm:MM_REWARD"].Name);
            }
            using var reopened = new SessionCoordinator(session, startingGame: Game.Mm);
            Invoke(reopened, "SyncCompletedFromState", true);
            Invoke(reopened, "HandleCheckCollected", "mm:MM_REWARD", true);
            Assert.Single(reopened.CompletedChecks);
            Assert.Equal("Fairy Ocarina", Rewards(reopened)["mm:MM_REWARD"].Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CheckRewardRejectsAnotherGamesRequestAndFailedDurability()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            using var coordinator = new SessionCoordinator(session, startingGame: Game.Oot);
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);
            Assert.Empty(coordinator.CompletedChecks);
            Assert.Empty(Rewards(coordinator));
            var blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(blocker, "file blocks state directory");
            typeof(OotMM.Core.Session).GetProperty("StatePath")!.SetValue(session, Path.Combine(blocker, "state.json"));
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", false);
            Assert.Empty(coordinator.CompletedChecks);
            Assert.Empty(Rewards(coordinator));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CheckAliasesResolveOnceToTheCanonicalDurableReward()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            using (var coordinator = new SessionCoordinator(session, startingGame: Game.Mm))
            {
                Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD_ALIAS", true);
                Invoke(coordinator, "HandleCheckCollected", "mm:Reward", true);
                Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);
                Assert.Equal(["mm:MM_REWARD"], coordinator.CompletedChecks);
                Assert.Single(Rewards(coordinator));
                Assert.Equal("Fairy Ocarina", Rewards(coordinator)["mm:MM_REWARD"].Name);
                var inventory = (SessionInventory)typeof(SessionCoordinator)
                    .GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
                Invoke(coordinator, "ReconcileInventory");
                Assert.Equal(1u, inventory.Items["OOT_OCARINA"]);
            }
            using var reopened = new SessionCoordinator(session, startingGame: Game.Mm);
            Invoke(reopened, "SyncCompletedFromState", true);
            Invoke(reopened, "HandleCheckCollected", "mm:MM_REWARD_ALIAS", true);
            Assert.Equal(["mm:MM_REWARD"], reopened.CompletedChecks);
            Assert.Equal("Fairy Ocarina", Rewards(reopened)["mm:MM_REWARD"].Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CheckRewardCannotAcceptAPlacementFromAnotherProgressionWorld()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory, sourcePlayer: 2);
            using var coordinator = new SessionCoordinator(session, startingGame: Game.Mm);
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD", true);
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD_ALIAS", false);
            Assert.Empty(coordinator.CompletedChecks);
            Assert.Empty(Rewards(coordinator));
            Assert.False(File.Exists(session.StatePath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LegacyAliasesAndMalformedResolutionDoNotLoseValidDurableProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reward-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            File.WriteAllText(session.StatePath, """
                {"completedChecks":["mm:MM_REWARD_ALIAS","mm:Reward","mm:UNKNOWN"],
                 "resolvedCheckRewards":{
                   "mm:Reward":7,
                   "mm:MM_REWARD":{"Game":0,"Id":"OOT_OCARINA","Name":null},
                   "mm:MM_REWARD_ALIAS":{"Game":0,"Id":"OOT_OCARINA","Name":"Fairy Ocarina"}}}
                """);
            using var coordinator = new SessionCoordinator(session, startingGame: Game.Mm);
            Invoke(coordinator, "SyncCompletedFromState", true);
            Assert.Equal(["mm:MM_REWARD"], coordinator.CompletedChecks);
            Assert.Equal("Fairy Ocarina", Rewards(coordinator)["mm:MM_REWARD"].Name);
            Invoke(coordinator, "HandleCheckCollected", "mm:MM_REWARD_ALIAS", true);
            Assert.Single(Rewards(coordinator));
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(session.StatePath));
            Assert.Equal("mm:MM_REWARD", saved.RootElement.GetProperty("completedChecks")[0].GetString());
            Assert.Single(saved.RootElement.GetProperty("resolvedCheckRewards").EnumerateObject());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ForceUnlockRecordsAndGrantsTheCheckWithoutARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-force-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            using var coordinator = new SessionCoordinator(session, startingGame: Game.Oot);
            var inventory = (SessionInventory)typeof(SessionCoordinator)
                .GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;

            Assert.False(coordinator.ForceUnlock("mm:MM_UNKNOWN"));
            Assert.True(coordinator.ForceUnlock("mm:MM_REWARD_ALIAS"));
            Assert.False(coordinator.ForceUnlock("mm:MM_REWARD"));

            Assert.Equal(["mm:MM_REWARD"], coordinator.CompletedChecks);
            Assert.Equal(1u, inventory.Items.GetValueOrDefault("OOT_OCARINA"));
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(session.StatePath));
            Assert.Equal("mm:MM_REWARD", saved.RootElement.GetProperty("completedChecks")[0].GetString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static OotMM.Core.Session RewardSession(string directory, ushort sourcePlayer = 1)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat, GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "reward-retry",
            Placements =
            [
                new(new CheckRef(Game.Mm, "MM_REWARD", "Reward", ["MM_REWARD_ALIAS"], null, "npc", null),
                    new ItemRef(Game.Oot, "OOT_OCARINA", "Progressive Ocarina"), null, 1, sourcePlayer, true),
            ],
        };
        var session = new OotMM.Core.Session
        {
            Seed = seed, SeedPath = "unused-seed.json", OotExe = "unused-soh.exe", MmExe = "unused-2ship.exe",
        };
        typeof(OotMM.Core.Session).GetProperty("StatePath")!.SetValue(session, Path.Combine(directory, "state.json"));
        typeof(OotMM.Core.Session).GetProperty("DebugItemsPath")!.SetValue(session, Path.Combine(directory, "debug.json"));
        return session;
    }

    private static Dictionary<string, ItemRef> Rewards(SessionCoordinator coordinator) =>
        (Dictionary<string, ItemRef>)typeof(SessionCoordinator)
            .GetField("_resolvedCheckRewards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;

    private static void Invoke(SessionCoordinator coordinator, string method, params object[] args) =>
        typeof(SessionCoordinator).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(coordinator, args);

    [Fact]
    public void SoloResetDropsTheOtherGamesPurchaseVitals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ootmm-reset-vitals-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var session = RewardSession(directory);
            foreach (var (property, file) in new[]
                     {
                         ("SessionDir", ""), ("OotInbox", "oot.inbox"), ("OotOutbox", "oot.outbox"),
                         ("MmInbox", "mm.inbox"), ("MmOutbox", "mm.outbox"),
                         ("OotOutboxCursor", "oot.cursor"), ("MmOutboxCursor", "mm.cursor"),
                     })
            {
                typeof(OotMM.Core.Session).GetProperty(property)!.SetValue(session, Path.Combine(directory, file));
            }
            string[] dropped = ["state.json.vitals.oot", "state.json.vitals.mm", "state.json.vitals.oot.used"];
            foreach (var name in dropped.Append("unrelated.json"))
            {
                File.WriteAllText(Path.Combine(directory, name), "{}");
            }
            using var coordinator = new SessionCoordinator(session, startingGame: Game.Mm);

            Assert.True(coordinator.ResetSeedProgress());

            Assert.All(dropped, name => Assert.False(File.Exists(Path.Combine(directory, name))));
            Assert.True(File.Exists(Path.Combine(directory, "unrelated.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void RefusedMultiworldResetPreservesPendingStartup(int players)
    {
        var seed = new Seed
        {
            Format = Seed.CurrentFormat, GeneratorVersion = Seed.RequiredGeneratorVersion,
            SeedId = "reset-refusal", PlayerCount = (ushort)players,
        };
        var session = new OotMM.Core.Session
        {
            Seed = seed, SeedPath = "unused-seed.json", OotExe = "unused-soh.exe", MmExe = "unused-2ship.exe",
        };
        using var startup = new CancellationTokenSource();
        using var coordinator = new SessionCoordinator(session);
        var field = typeof(SessionCoordinator).GetField("_startupCancellation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(coordinator, startup);
        var messages = new List<string>();
        coordinator.EventLogged += messages.Add;

        Assert.False(coordinator.ResetSeedProgress());
        Assert.False(startup.IsCancellationRequested);
        Assert.Contains(messages, message => message.StartsWith("Reset refused:"));
    }

    [Theory]
    [InlineData(unchecked((int)0xC0000005), 3, 0, true)]
    [InlineData(unchecked((int)0xC0000005), 3, 1, true)]
    [InlineData(unchecked((int)0xC0000005), 3, 2, false)]
    [InlineData(unchecked((int)0xC0000005), 45, 0, false)]
    [InlineData(0, 3, 0, false)]
    public void StartupCrashesRelaunchTheSameEntryALimitedNumberOfTimes(int exitCode, int seconds, int retries,
                                                                       bool expected)
    {
        Assert.Equal(expected, SessionCoordinator.ShouldRelaunchAfterStartupCrash(
            exitCode, TimeSpan.FromSeconds(seconds), retries));
        Assert.False(SessionCoordinator.ShouldRelaunchAfterStartupCrash(null, TimeSpan.Zero, 0));
    }
}
