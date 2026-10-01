using System.Security.Cryptography;
using System.Text.Json;
using OotMM.Core;

namespace OotMM.Core.Tests;

public sealed class MmSaveSourceTests : IDisposable
{
    private const string Id = "11111111111111111111111111111111";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ootmm-recovery-" + Guid.NewGuid().ToString("N"));
    private string Primary => Path.Combine(_directory, "file3.json");
    private string Backup => Path.Combine(_directory, "file3backup.json");

    public MmSaveSourceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private static string Payload(string section, int revision, int ammo, string id = Id) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [section] = new
            {
                save = new { retainedNativeField = "native loader validates this" },
                ootmmSharedResources = new
                {
                    version = 1, saveId = id, slot = 2, revision, source = "mm",
                    values = new { bombchu = ammo }, applied = new { SHARED_BOMBCHU_BAG = 1 },
                },
            },
        });

    [Fact]
    public void ValidCyclePrimaryWinsOverHistoricalBackupOwl()
    {
        File.WriteAllText(Primary, Payload("newCycleSave", 12, 0));
        File.WriteAllText(Backup, Payload("owlSave", 11, 19));
        Assert.True(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out var error), error);
        Assert.False(result!.FromBackup);
        Assert.Equal(0u, result.SharedResources!.Values["bombchu"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrTruncatedPrimarySelectsBackupWithoutChangingEitherFile(bool truncate)
    {
        if (truncate) File.WriteAllText(Primary, "{\"owlSave\":");
        var original = truncate ? File.ReadAllBytes(Primary) : null;
        var backup = Payload("owlSave", 12, 7);
        File.WriteAllText(Backup, backup);
        Assert.True(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out var error), error);
        Assert.True(result!.FromBackup);
        Assert.Equal(backup, result.Contents);
        Assert.Equal(Backup, result.Path);
        Assert.Equal(7u, result.SharedResources!.Values["bombchu"]);
        Assert.Equal(original is null ? null : Convert.ToHexStringLower(SHA256.HashData(original)), result.DamagedPrimarySha256);
        Assert.Equal(backup, File.ReadAllText(Backup));
        if (truncate) Assert.Equal(original, File.ReadAllBytes(Primary));
        else Assert.False(File.Exists(Primary));
    }

    [Theory]
    [InlineData("{\"owlSave\":{},\"owlSave\":{}}")]
    [InlineData("{\"owlSave\":null}")]
    [InlineData("{}")]
    public void AmbiguousOrInvalidPrimaryDoesNotSilentlyChooseBackup(string primary)
    {
        File.WriteAllText(Primary, primary);
        File.WriteAllText(Backup, Payload("owlSave", 2, 7));
        Assert.False(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out _));
        Assert.Null(result);
        Assert.Equal(primary, File.ReadAllText(Primary));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongIdentityInPrimaryOrBackupRejectsRecovery(bool backup)
    {
        File.WriteAllText(Primary, backup ? "{" : Payload("owlSave", 2, 7, new string('2', 32)));
        File.WriteAllText(Backup, Payload("owlSave", 2, 7, backup ? new string('2', 32) : Id));
        Assert.False(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void LockedPrimaryDoesNotFallBackToOlderBackup()
    {
        File.WriteAllText(Primary, Payload("owlSave", 3, 6));
        File.WriteAllText(Backup, Payload("owlSave", 2, 7));
        using var locked = new FileStream(Primary, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.False(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void NoFilesIsNewSaveButCorruptPrimaryWithoutBackupIsNot()
    {
        Assert.True(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out _));
        Assert.Null(result);
        File.WriteAllText(Primary, "{");
        Assert.False(MmSaveSource.TrySelect(Primary, Id, 2, out result, out _));
        Assert.Null(result);
    }

    [Fact]
    public void CorruptBackupIsNotANewSave()
    {
        File.WriteAllText(Backup, "{");
        Assert.False(MmSaveSource.TrySelect(Primary, Id, 2, out var result, out _));
        Assert.Null(result);
    }
}
