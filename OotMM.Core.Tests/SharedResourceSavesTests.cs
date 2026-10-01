using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class SharedResourceSavesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ootmm-shared-resources", Guid.NewGuid().ToString("N"));
    private string Oot => Path.Combine(_directory, "file1.sav");
    private string Mm => Path.Combine(_directory, "file1.json");

    public SharedResourceSavesTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Snapshot(int revision, int ammo, string source = "mm") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1, saveId = "save", slot = 0, revision, source,
            values = new { bombchu = ammo }, applied = new { SHARED_BOMBCHU_BAG = 1 },
        });

    [Fact]
    public void ReadsConsumedStateFromNativeSaveWithoutAnIpcAcknowledgement()
    {
        File.WriteAllText(Oot, "{\"ootmmSharedResources\":" + Snapshot(1, 20, "oot") + "}");
        File.WriteAllText(Mm, "{\"owlSave\":{\"ootmmSharedResources\":" + Snapshot(3, 19)
            + "},\"newCycleSave\":{\"ootmmSharedResources\":" + Snapshot(2, 20) + "}}");
        Assert.True(SharedResourceSaves.TryRead(Oot, Mm, "save", 0, out var state, out var error), error);
        Assert.Equal(3ul, state!.Revision);
        Assert.Equal(19u, state.Values["bombchu"]);
    }

    [Fact]
    public void InterruptedTemporarySaveDoesNotSupersedeCommittedState()
    {
        File.WriteAllText(Oot, "{\"ootmmSharedResources\":" + Snapshot(1, 20, "oot") + "}");
        File.WriteAllText(Mm + ".tmp", "{\"owlSave\":{\"ootmmSharedResources\":" + Snapshot(2, 19) + "}}");
        Assert.True(SharedResourceSaves.TryRead(Oot, Mm, "save", 0, out var state, out _));
        Assert.Equal(20u, state!.Values["bombchu"]);
    }

    [Fact]
    public void MissingAndLegacySavesHaveNoSharedSnapshot()
    {
        File.WriteAllText(Oot, "{\"sections\":{}}");
        Assert.True(SharedResourceSaves.TryRead(Oot, Mm, "save", 0, out var state, out _));
        Assert.Null(state);
    }

    [Theory]
    [InlineData(true, 19u)]
    [InlineData(false, 20u)]
    public void UsesSelectedMmContentsEvenIfAnotherSaveAppearsBeforeConsumption(bool selectedMm, uint expectedAmmo)
    {
        File.WriteAllText(Oot, "{\"ootmmSharedResources\":" + Snapshot(1, 20, "oot") + "}");
        var selected = selectedMm ? "{\"owlSave\":{\"ootmmSharedResources\":" + Snapshot(2, 19) + "}}" : "{}";
        File.WriteAllText(Mm, "{\"owlSave\":{\"ootmmSharedResources\":" + Snapshot(99, 50) + "}}");
        var before = File.ReadAllBytes(Mm);
        Assert.True(SharedResourceSaves.TryRead(Oot, Mm, "save", 0, out var state, out var error, selected), error);
        Assert.Equal(expectedAmmo, state!.Values["bombchu"]);
        Assert.Equal(before, File.ReadAllBytes(Mm));
    }

    [Theory]
    [InlineData("{\"owlSave\":")]
    [InlineData("{\"owlSave\":null}")]
    [InlineData("{\"owlSave\":{\"ootmmSharedResources\":null}}")]
    [InlineData("{\"owlSave\":{},\"owlSave\":{}}")]
    [InlineData("{\"owlSave\":{\"ootmmSharedResources\":null,\"ootmmSharedResources\":null}}")]
    public void CorruptSnapshotDoesNotFallBackToOlderAmmo(string contents)
    {
        File.WriteAllText(Oot, "{\"ootmmSharedResources\":" + Snapshot(1, 20, "oot") + "}");
        File.WriteAllText(Mm, contents);
        Assert.False(SharedResourceSaves.TryRead(Oot, Mm, "save", 0, out var state, out var error));
        Assert.Null(state);
        Assert.NotEmpty(error);
    }
}
