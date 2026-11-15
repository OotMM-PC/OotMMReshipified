using System.IO.Compression;
using OotMM.Launcher.Session;

namespace OotMM.Launcher.Tests;

public sealed class CrashReportsTests
{
    [Theory]
    [InlineData(unchecked((int)0xC0000374), true)]
    [InlineData(unchecked((int)0xC0000005), true)]
    [InlineData(unchecked((int)0xE06D7363), true)]
    [InlineData(0x40000015, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void OnlyFatalExitCodesCountAsCrashes(int exitCode, bool expected) =>
        Assert.Equal(expected, CrashReports.IsCrashExit(exitCode));

    [Fact]
    public void FindsTheReportOfThisRunAndBundlesItWithItsDump()
    {
        var game = Path.Combine(Path.GetTempPath(), "ootmm-crash-" + Guid.NewGuid());
        var crashes = Path.Combine(game, "crashes");
        Directory.CreateDirectory(crashes);
        try
        {
            var exe = Path.Combine(game, "2ship.exe");
            var launched = DateTime.UtcNow;
            var stale = Path.Combine(crashes, "2s2h-20260101-000000-4242.txt");
            File.WriteAllText(stale, "old\nSummary: an older run\n");
            File.SetLastWriteTimeUtc(stale, launched.AddHours(-2));
            File.WriteAllText(Path.Combine(crashes, "2s2h-20261114-211107-9999.txt"), "x\nSummary: another process\n");
            Assert.Null(CrashReports.Find(exe, 4242, launched));

            var report = Path.Combine(crashes, "2s2h-20261114-211107-4242.txt");
            File.WriteAllText(report,
                "2 Ship 2 Harkinian crash report\nSummary: Heap corruption (0xC0000374) at ntdll.dll+0x118095\n");
            File.WriteAllBytes(Path.ChangeExtension(report, ".dmp"), new byte[4096]);

            var found = CrashReports.Find(exe, 4242, launched);
            Assert.NotNull(found);
            Assert.Equal(report, found.ReportPath);
            Assert.Equal("Heap corruption (0xC0000374) at ntdll.dll+0x118095", found.Summary);

            var bundled = CrashReports.Bundle(found);
            Assert.Equal(Path.ChangeExtension(report, ".zip"), bundled.SharePath);
            Assert.False(File.Exists(Path.ChangeExtension(report, ".dmp")));
            using (var archive = ZipFile.OpenRead(bundled.SharePath))
            {
                Assert.Equal(["2s2h-20261114-211107-4242.dmp", "2s2h-20261114-211107-4242.txt"],
                    archive.Entries.Select(entry => entry.Name).Order());
            }

            Assert.Equal(bundled.SharePath, CrashReports.Bundle(found).SharePath);
            Assert.Equal(
                $"Majora's Mask crashed: Heap corruption (0xC0000374) at ntdll.dll+0x118095. Crash report: {bundled.SharePath}",
                CrashReports.Describe("Majora's Mask", exe, unchecked((int)0xC0000374), bundled));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void DescribesACrashThatLeftNoReportYet()
    {
        var exe = Path.Combine(Path.GetTempPath(), "ootmm-none-" + Guid.NewGuid(), "soh.exe");
        Assert.Null(CrashReports.Find(exe, 1, DateTime.UtcNow));
        Assert.Equal(
            $"Ocarina of Time stopped unexpectedly (exit code 0xC0000409). Any crash report is saved in {CrashReports.DirectoryFor(exe)}",
            CrashReports.Describe("Ocarina of Time", exe, unchecked((int)0xC0000409), null));
    }
}
