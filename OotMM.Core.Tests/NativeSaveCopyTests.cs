using System.Text.Json;
using System.Text.Json.Nodes;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class NativeSaveCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ootmm-paired-copy", Guid.NewGuid().ToString("N"));
    private readonly Seed _seed = new()
    {
        Format = Seed.CurrentFormat, GeneratorVersion = Seed.RequiredGeneratorVersion,
        SeedId = "seed", SettingsHash = "copy", TeamId = "team", PlayerId = 1, PlayerCount = 1,
    };
    private Session Create() => new()
    {
        Seed = _seed, SeedPath = Path.Combine(_root, "seed.json"),
        OotExe = Path.Combine(_root, "oot", "soh.exe"), MmExe = Path.Combine(_root, "mm", "2ship.exe"),
    };
    private readonly Session _source;
    private readonly Session _target;
    private string Oot(uint slot) => Path.Combine(_root, "oot", "Save", "ootmm-copy", $"file{slot + 1}.sav");
    private string Mm(uint slot) => Path.Combine(_root, "mm", "saves", "ootmm-copy", $"file{slot + 1}.json");

    public NativeSaveCopyTests()
    {
        Directory.CreateDirectory(_root);
        _source = Create();
        Assert.Null(SessionPaths.PrepareSession(_source));
        Assert.True(SessionPaths.TryPrepareSelectedSession(_source,
            new NativeSaveRequest(1, NativeSaveOperation.Select, _source.SaveId, 0, 0, 1), 0, out var target, out _));
        _target = target!;
        Directory.CreateDirectory(Path.GetDirectoryName(Oot(0))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Mm(0))!);
        var shared = JsonSerializer.SerializeToNode(new
        {
            version = 1, saveId = _source.SaveId, slot = 0, revision = 7, source = "mm",
            values = new { bombchu = 8 }, applied = new { SHARED_BOMBCHU_BAG = 1 },
        });
        File.WriteAllText(Oot(0), new JsonObject
        {
            ["sections"] = new JsonObject { ["base"] = new JsonObject { ["data"] = new JsonObject { ["ammo"] = 8 } } },
            ["ootmmSharedResources"] = shared!.DeepClone(),
        }.ToJsonString());
        File.WriteAllText(Mm(0), new JsonObject
        {
            ["owlSave"] = new JsonObject { ["save"] = new JsonObject { ["ammo"] = 8 }, ["ootmmSharedResources"] = shared },
        }.ToJsonString());
        File.WriteAllText(_source.StatePath, """{"completedChecks":["oot:example"],"sessionValues":{"ootmm.activeSlot":"0"}}""");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void CopiesBothGamesAndCheckStateUnderIndependentIdentity()
    {
        var original = File.ReadAllBytes(Oot(0));
        File.WriteAllText(_target.MmInbox, "stale delivery");
        Assert.True(NativeSaveCopy.TryExecute(_source, _target, out var error), error);
        Assert.Equal(original, File.ReadAllBytes(Oot(0)));
        var oot = JsonNode.Parse(File.ReadAllText(Oot(1)))!;
        var mm = JsonNode.Parse(File.ReadAllText(Mm(1)))!;
        Assert.Equal(_target.SaveId, oot["ootmmSharedResources"]!["saveId"]!.GetValue<string>());
        Assert.Equal(_target.SaveId, mm["owlSave"]!["ootmmSharedResources"]!["saveId"]!.GetValue<string>());
        Assert.Equal(1, mm["owlSave"]!["ootmmSharedResources"]!["slot"]!.GetValue<int>());
        Assert.Equal(File.ReadAllBytes(Mm(1)), File.ReadAllBytes(Mm(1).Replace(".json", "backup.json")));
        Assert.Equal(1u, SessionOwnership.ParseRuntimeLogicalSlot(File.ReadAllText(_target.StatePath), out _));
        Assert.Contains("oot:example", File.ReadAllText(_target.StatePath));
        Assert.Equal("", File.ReadAllText(_target.MmInbox));
        Assert.Equal(0L, DurableFile.ReadOffset(_target.MmOutboxCursor));
    }

    [Fact]
    public void LockedMmDestinationRollsBackEarlierOotReplacement()
    {
        File.WriteAllText(Oot(1), "old oot");
        File.WriteAllText(Mm(1), "old mm");
        File.WriteAllText(_target.StatePath, "old state");
        using var locked = new FileStream(Mm(1), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.False(NativeSaveCopy.TryExecute(_source, _target, out _));
        Assert.Equal("old oot", File.ReadAllText(Oot(1)));
        Assert.Equal("old mm", File.ReadAllText(Mm(1)));
        Assert.Equal("old state", File.ReadAllText(_target.StatePath));
    }

    [Fact]
    public void MalformedSourceDoesNotTouchEitherDestination()
    {
        File.WriteAllText(Mm(0), "{");
        File.WriteAllText(Oot(1), "old oot");
        Assert.False(NativeSaveCopy.TryExecute(_source, _target, out _));
        Assert.Equal("old oot", File.ReadAllText(Oot(1)));
        Assert.False(File.Exists(Mm(1)));
    }

    [Fact]
    public void PendingCopyRecoveryBlocksStartupEvenWhenDestinationIsNotSelected()
    {
        File.WriteAllText(Path.Combine(_target.SessionDir, "save-copy.pending.json"), "{");
        Assert.NotNull(SessionPaths.PrepareSession(Create()));
        Assert.True(File.Exists(Path.Combine(_target.SessionDir, "save-copy.pending.json")));
    }
}
