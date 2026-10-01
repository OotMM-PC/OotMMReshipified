using System.Text;
using System.Text.Json;
using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public sealed class SaveCopyTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ootmm-copy", Guid.NewGuid().ToString("N"));
    private string A => Path.Combine(_root, "oot.sav");
    private string B => Path.Combine(_root, "mm.json");
    private string Journal => Path.Combine(_root, "copy.json");
    private SaveCopyTransaction Transaction => new(Journal, new Dictionary<string, string> { ["oot"] = A, ["mm"] = B });
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    public SaveCopyTransactionTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void CommitsBothSavesAndRetiresJournal()
    {
        File.WriteAllText(A, "old oot");
        Assert.True(Transaction.Execute(new Dictionary<string, byte[]?>
            { ["oot"] = Bytes("new oot"), ["mm"] = Bytes("new mm") }, out var error), error);
        Assert.Equal("new oot", File.ReadAllText(A));
        Assert.Equal("new mm", File.ReadAllText(B));
        Assert.False(File.Exists(Journal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedSecondSaveRestoresFirstSaveAndPreservesLockedBytes(bool firstExisted)
    {
        if (firstExisted) File.WriteAllText(A, "old oot");
        File.WriteAllText(B, "old mm");
        using var locked = new FileStream(B, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.False(Transaction.Execute(new Dictionary<string, byte[]?>
            { ["oot"] = Bytes("new oot"), ["mm"] = Bytes("new mm") }, out _));
        Assert.Equal(firstExisted, File.Exists(A));
        if (firstExisted) Assert.Equal("old oot", File.ReadAllText(A));
        Assert.Equal("old mm", File.ReadAllText(B));
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public void InterruptedPreparedCopyRestoresOriginalSetOnReopen()
    {
        File.WriteAllText(A, "partially copied oot");
        File.WriteAllText(B, "newly created mm");
        File.WriteAllText(Journal, JsonSerializer.Serialize(new
        {
            Version = 1, Committed = false,
            Before = new Dictionary<string, byte[]?> { ["oot"] = Bytes("original oot"), ["mm"] = null },
        }));
        Assert.True(Transaction.Recover(out var error), error);
        Assert.Equal("original oot", File.ReadAllText(A));
        Assert.False(File.Exists(B));
        Assert.True(Transaction.Recover(out error), error);
    }

    [Fact]
    public void CommittedJournalCannotOverwriteSubsequentProgress()
    {
        File.WriteAllText(A, "later progress");
        File.WriteAllText(B, "copied mm");
        File.WriteAllText(Journal, JsonSerializer.Serialize(new
        {
            Version = 1, Committed = true,
            Before = new Dictionary<string, byte[]?> { ["oot"] = Bytes("old oot"), ["mm"] = null },
        }));
        Assert.True(Transaction.Recover(out var error), error);
        Assert.Equal("later progress", File.ReadAllText(A));
        Assert.Equal("copied mm", File.ReadAllText(B));
    }

    [Fact]
    public void UnknownJournalDestinationFailsBeforeAnyRestore()
    {
        File.WriteAllText(A, "preserve");
        File.WriteAllText(Journal, JsonSerializer.Serialize(new
        {
            Version = 1, Committed = false,
            Before = new Dictionary<string, byte[]?> { ["oot"] = Bytes("wrong"), ["outside"] = null },
        }));
        Assert.False(Transaction.Recover(out _));
        Assert.Equal("preserve", File.ReadAllText(A));
        Assert.True(File.Exists(Journal));
    }

    [Fact]
    public void BlockedRecoveryRetainsEvidenceAndSucceedsAfterUnlock()
    {
        File.WriteAllText(A, "partial copy");
        File.WriteAllText(Journal, JsonSerializer.Serialize(new
        {
            Version = 1, Committed = false,
            Before = new Dictionary<string, byte[]?> { ["oot"] = Bytes("original"), ["mm"] = null },
        }));
        var evidence = File.ReadAllBytes(Journal);
        using (var locked = new FileStream(A, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(Transaction.Recover(out _));
            Assert.Equal(evidence, File.ReadAllBytes(Journal));
            Assert.Equal("partial copy", File.ReadAllText(A));
        }
        Assert.True(Transaction.Recover(out var error), error);
        Assert.Equal("original", File.ReadAllText(A));
        Assert.False(File.Exists(Journal));
    }
}
