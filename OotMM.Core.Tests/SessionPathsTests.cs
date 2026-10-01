using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class SessionPathsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ootmm-session-tests", Guid.NewGuid().ToString("N"));

    public SessionPathsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "oot"));
        Directory.CreateDirectory(Path.Combine(_dir, "mm"));
        Directory.CreateDirectory(Path.Combine(_dir, "seeds"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Seed MakeSeed(ushort playerId = 2, ushort playerCount = 3, ulong epoch = 1) => new()
    {
        Format = Seed.CurrentFormat,
        GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "SEEDID",
        SettingsHash = "abc123",
        TeamId = "team",
        PlayerId = playerId,
        PlayerCount = playerCount,
        SessionEpoch = epoch,
    };

    private Session MakeSession(Seed seed, bool v4) => new()
    {
        SeedPath = Path.Combine(_dir, "seeds", "test.ootmm.json"),
        OotExe = Path.Combine(_dir, "oot", "soh.exe"),
        MmExe = Path.Combine(_dir, "mm", "2ship.exe"),
        Seed = seed,
        V4Planned = v4,
    };

    [Fact]
    public void Tags_MatchCppFormats()
    {
        var seed = MakeSeed(playerId: 2, playerCount: 3, epoch: 4);
        Assert.Equal("abc123", SessionPaths.SeedTag(seed));
        Assert.Equal("abc123_p2_e4", SessionPaths.SaveTag(seed));

        var solo = MakeSeed(playerId: 1, playerCount: 1);
        Assert.Equal("abc123", SessionPaths.SaveTag(solo));

        var session = MakeSession(seed, v4: true);
        Assert.Equal("abc123_p2_v4", SessionPaths.SessionBindingKey(session, "abc123"));
        Assert.Equal("_v4", SessionPaths.SessionDirSuffix(session));
        Assert.EndsWith(Path.Combine("Save", "ootmm-v4"), SessionPaths.OotNativeSavesRoot(session));
        Assert.EndsWith(Path.Combine("saves", "ootmm-v4"), SessionPaths.MmNativeSavesRoot(session));
    }

    [Fact]
    public void PrepareSession_FreshThenResume()
    {
        var session = MakeSession(MakeSeed(), v4: true);
        Assert.Null(SessionPaths.PrepareSession(session));

        Assert.True(SessionPaths.IsValidSaveId(session.SaveId));
        var dirName = Path.GetFileName(session.SessionDir);
        Assert.Equal($"abc123_p2_v4_{session.SaveId}_v4", dirName);
        Assert.True(File.Exists(Path.Combine(session.SessionDir, "session.id")));
        Assert.Equal(0L, DurableFile.ReadOffset(session.OotOutboxCursor));
        Assert.True(SessionOwnership.ValidateMarker(
            File.ReadAllText(Path.Combine(session.SessionDir, "session.id")),
            session.Seed, dirName));

        // Progress written between launches must survive the resume path.
        File.WriteAllText(session.StatePath, """{"sessionValues":{"ootmm.activeSlot":"0"}}""");
        var resumed = MakeSession(MakeSeed(), v4: true);
        Assert.Null(SessionPaths.PrepareSession(resumed));
        Assert.Equal(session.SaveId, resumed.SaveId);
        Assert.Equal(session.SessionDir, resumed.SessionDir);
        Assert.True(File.Exists(resumed.StatePath));
        Assert.Equal(0u, resumed.LogicalSlot);
    }

    [Fact]
    public void PrepareSession_SoloUsesV3Namespace()
    {
        var session = MakeSession(MakeSeed(playerId: 1, playerCount: 1), v4: false);
        Assert.Null(SessionPaths.PrepareSession(session));
        Assert.EndsWith("_v3", Path.GetFileName(session.SessionDir));
        Assert.DoesNotContain("_v4", Path.GetFileName(session.SessionDir));
    }

    [Fact]
    public void PrepareSession_RecoversPendingTransition()
    {
        var seed = MakeSeed();
        var session = MakeSession(seed, v4: true);
        Assert.Null(SessionPaths.PrepareSession(session));

        var packet = "{\"type\":\"OOTMM_CROSS_GAME_TRANSITION\",\"seedId\":\"SEEDID\","
                     + "\"settingsHash\":\"abc123\",\"teamId\":\"team\",\"playerId\":2,"
                     + "\"fromGame\":\"oot\",\"toGame\":\"mm\",\"toEntrance\":\"MM_CLOCK_TOWN\","
                     + "\"toNativeId\":3446,\"ootAge\":1}";
        File.WriteAllText(session.PendingTransitionPath, packet);

        var resumed = MakeSession(seed, v4: true);
        Assert.Null(SessionPaths.PrepareSession(resumed));
        Assert.NotNull(resumed.RecoveredTransition);
        Assert.Equal(Game.Mm, resumed.RecoveredTransition!.ToGame);
        Assert.Equal(3446u, resumed.RecoveredTransition.ToNativeId);

        // A transition belonging to another seed must fail the resume, not boot wrong.
        File.WriteAllText(session.PendingTransitionPath, packet.Replace("SEEDID", "OTHER"));
        Assert.NotNull(SessionPaths.PrepareSession(MakeSession(seed, v4: true)));
    }

    [Fact]
    public void MakeBootConfig_MapsSessionFields()
    {
        var session = MakeSession(MakeSeed(), v4: true);
        Assert.Null(SessionPaths.PrepareSession(session));

        var boot = SessionPaths.MakeBootConfig(session, Game.Mm, bootEntrance: 3446, ootAge: 1);
        Assert.Equal(session.SaveId, boot.SaveId);
        Assert.Equal(session.MmInbox, boot.InboxPath);
        Assert.Equal(session.MmOutbox, boot.OutboxPath);
        Assert.Equal(SessionPaths.MmNativeSavesRoot(session), boot.MmSaveDir);
        Assert.Equal(SessionPaths.V4NativeSaveSubdir, boot.NativeSaveSubdir);
        Assert.Equal(4, boot.MultiworldProtocol);
        Assert.Equal(Game.Mm, boot.BootGame);
        Assert.Equal(3446u, boot.BootEntrance);
        Assert.Equal(session.LogicalSlot, boot.LogicalSlot);

        var parsed = BootConfig.Parse(boot.Serialize());
        Assert.NotNull(parsed);
        Assert.Equal(boot.Serialize(), parsed!.Serialize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedSessionUsesIndependentIdentityAndPreservesBothSlots(bool v4)
    {
        var current = MakeSession(MakeSeed(), v4);
        Assert.Null(SessionPaths.PrepareSession(current));
        const string sourceState = """{"sessionValues":{"ootmm.activeSlot":"0"},"checks":["original"]}""";
        File.WriteAllText(current.StatePath, sourceState);
        var request = new NativeSaveRequest(1, NativeSaveOperation.Select, current.SaveId, 0, 0, 1);
        Assert.True(SessionPaths.TryPrepareSelectedSession(current, request, 0, out var target, out var error), error);
        Assert.NotEqual(current.SaveId, target!.SaveId);
        Assert.NotEqual(current.SessionDir, target.SessionDir);
        Assert.Equal(1u, target.LogicalSlot);
        Assert.Equal(0u, current.LogicalSlot);
        Assert.Equal(sourceState, File.ReadAllText(current.StatePath));
        Assert.False(File.Exists(target.StatePath));
        const string targetState = """{"sessionValues":{"ootmm.activeSlot":"1"},"checks":["target"]}""";
        File.WriteAllText(target.StatePath, targetState);
        Assert.True(SessionPaths.TryPrepareSelectedSession(current, request with { RequestId = 2 }, 1,
            out var resumed, out error), error);
        Assert.Equal(target.SaveId, resumed!.SaveId);
        Assert.Equal(targetState, File.ReadAllText(resumed.StatePath));
        var back = new NativeSaveRequest(3, NativeSaveOperation.Select, resumed.SaveId, 1, 1, 0);
        Assert.True(SessionPaths.TryPrepareSelectedSession(resumed, back, 2, out var original, out error), error);
        Assert.Equal(current.SaveId, original!.SaveId);
        Assert.Equal(sourceState, File.ReadAllText(original.StatePath));
    }

    [Fact]
    public void InvalidSelectionDoesNotCreateTargetBinding()
    {
        var current = MakeSession(MakeSeed(), false);
        Assert.Null(SessionPaths.PrepareSession(current));
        var root = Path.GetDirectoryName(current.SessionDir)!;
        var before = Directory.GetFiles(root, "*.save-id");
        var request = new NativeSaveRequest(5, NativeSaveOperation.Select, "stale", 0, 0, 1);
        Assert.False(SessionPaths.TryPrepareSelectedSession(current, request, 0, out var target, out _));
        Assert.Null(target);
        Assert.Equal(before, Directory.GetFiles(root, "*.save-id"));
        Assert.False(SessionPaths.TryPrepareSelectedSession(current, request with { SaveId = current.SaveId }, 5,
            out _, out _));
        Assert.Equal(before, Directory.GetFiles(root, "*.save-id"));
    }

    [Fact]
    public void SelectedSessionRejectsStateFromAnotherSlotWithoutRewritingIt()
    {
        var current = MakeSession(MakeSeed(), false);
        Assert.Null(SessionPaths.PrepareSession(current));
        var request = new NativeSaveRequest(1, NativeSaveOperation.Select, current.SaveId, 0, 0, 2);
        Assert.True(SessionPaths.TryPrepareSelectedSession(current, request, 0, out var target, out _));
        const string wrongState = """{"sessionValues":{"ootmm.activeSlot":"0"}}""";
        File.WriteAllText(target!.StatePath, wrongState);
        Assert.False(SessionPaths.TryPrepareSelectedSession(current, request with { RequestId = 2 }, 1,
            out var rejected, out _));
        Assert.Null(rejected);
        Assert.Equal(wrongState, File.ReadAllText(target.StatePath));
        Assert.Equal(0u, current.LogicalSlot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedSelectionSurvivesNewerStateInAnotherSlot(bool v4)
    {
        var current = MakeSession(MakeSeed(), v4);
        Assert.Null(SessionPaths.PrepareSession(current));
        var request = new NativeSaveRequest(1, NativeSaveOperation.Select, current.SaveId, 0, 0, 1);
        Assert.True(SessionPaths.TryPrepareSelectedSession(current, request, 0, out var target, out _));
        Assert.True(SessionPaths.RecordSelectedSession(target!, out var error), error);
        File.WriteAllText(current.StatePath, """{"sessionValues":{"ootmm.activeSlot":"0"}}""");
        File.SetLastWriteTimeUtc(current.StatePath, DateTime.UtcNow.AddHours(1));
        var resumed = MakeSession(MakeSeed(), v4);
        Assert.Null(SessionPaths.PrepareSession(resumed));
        Assert.Equal(1u, resumed.LogicalSlot);
        Assert.Equal(target!.SaveId, resumed.SaveId);
        Assert.Equal(target.SessionDir, resumed.SessionDir);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"version\":1,\"slot\":1,\"saveId\":\"wrong\"}")]
    [InlineData("{\"version\":1,\"slot\":4294967296,\"saveId\":\"wrong\"}")]
    [InlineData("{\"version\":1,\"slot\":0,\"slot\":1,\"saveId\":\"wrong\"}")]
    public void InvalidSelectedRecordCannotSilentlySelectAnotherSave(string invalid)
    {
        var current = MakeSession(MakeSeed(), false);
        Assert.Null(SessionPaths.PrepareSession(current));
        Assert.True(SessionPaths.RecordSelectedSession(current, out _));
        var marker = Directory.GetFiles(Path.GetDirectoryName(current.SessionDir)!, "*.selected-save.json").Single();
        File.WriteAllText(marker, invalid);
        Assert.NotNull(SessionPaths.PrepareSession(MakeSession(MakeSeed(), false)));
        Assert.Equal(invalid, File.ReadAllText(marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedSelectionReopensLegacyCheckOnlyJournalWithoutChangingProgress(bool v4)
    {
        var current = MakeSession(MakeSeed(), v4);
        Assert.Null(SessionPaths.PrepareSession(current));
        const string legacy = """{"completedChecks":["OOT_MIDOS_HOUSE_BOTTOM_RIGHT"]}""";
        File.WriteAllText(current.StatePath, legacy);
        Assert.True(SessionPaths.RecordSelectedSession(current, out _));
        var resumed = MakeSession(MakeSeed(), v4);
        Assert.Null(SessionPaths.PrepareSession(resumed));
        Assert.Equal(current.SaveId, resumed.SaveId);
        Assert.Equal(current.LogicalSlot, resumed.LogicalSlot);
        Assert.Equal(legacy, File.ReadAllText(resumed.StatePath));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"completedChecks\":[3]}")]
    [InlineData("{\"completedChecks\":[],\"completedChecks\":[]}")]
    [InlineData("{\"completedChecks\":[],\"sessionValues\":null}")]
    [InlineData("{\"completedChecks\":[],\"sessionValues\":{\"ootmm.activeSlot\":\"2\"}}")]
    public void RecordedSelectionRejectsMalformedOrConflictingLegacyJournal(string journal)
    {
        var current = MakeSession(MakeSeed(), false);
        Assert.Null(SessionPaths.PrepareSession(current));
        File.WriteAllText(current.StatePath, journal);
        Assert.True(SessionPaths.RecordSelectedSession(current, out _));
        Assert.NotNull(SessionPaths.PrepareSession(MakeSession(MakeSeed(), false)));
        Assert.Equal(journal, File.ReadAllText(current.StatePath));
    }
}
