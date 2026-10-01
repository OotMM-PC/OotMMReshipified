using OotMM.Core;
using Xunit;

namespace OotMM.Core.Tests;

public class DurableFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ootmm-durable-").FullName;

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

    private static Task HoldExclusively(string path, int milliseconds, ManualResetEventSlim held)
    {
        return Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            held.Set();
            Thread.Sleep(milliseconds);
        });
    }

    [Fact]
    public void TruncateSurvivesABriefSharingViolation()
    {
        var path = Path.Combine(_dir, "outbox.jsonl");
        File.WriteAllText(path, "pending\n");
        using var held = new ManualResetEventSlim();
        var holder = HoldExclusively(path, 120, held);
        held.Wait();

        Assert.True(DurableFile.TruncateDurably(path));
        holder.Wait();
        Assert.Equal(0, new FileInfo(path).Length);
    }

    [Fact]
    public void WriteAtomicSurvivesABriefSharingViolation()
    {
        var path = Path.Combine(_dir, "cursor");
        File.WriteAllText(path, "7\n");
        using var held = new ManualResetEventSlim();
        var holder = HoldExclusively(path, 120, held);
        held.Wait();

        Assert.True(DurableFile.WriteOffset(path, 0));
        holder.Wait();
        Assert.Equal(0L, DurableFile.ReadOffset(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void PersistentLocksStillFail()
    {
        var path = Path.Combine(_dir, "outbox.jsonl");
        File.WriteAllText(path, "pending\n");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(DurableFile.TruncateDurably(path));
            Assert.False(DurableFile.WriteAtomic(path, "replacement"));
        }

        Assert.Equal("pending\n", File.ReadAllText(path));
    }
}
