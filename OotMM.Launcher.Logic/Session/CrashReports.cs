using System.IO.Compression;

namespace OotMM.Launcher.Session;

public sealed record CrashReport(string ReportPath, string Summary, string? BundlePath)
{
    public string SharePath => BundlePath ?? ReportPath;
}

public static class CrashReports
{
    private const string SummaryPrefix = "Summary: ";
    private static readonly TimeSpan ClockSlack = TimeSpan.FromSeconds(5);

    public static string DirectoryFor(string exePath) =>
        Path.Combine(Path.GetDirectoryName(exePath) ?? "", "crashes");

    public static bool IsCrashExit(int exitCode)
    {
        var code = unchecked((uint)exitCode);
        return (code & 0xF0000000) is 0xC0000000 or 0xE0000000 || code is 0x80000003 or 0x40000015;
    }

    public static CrashReport? Find(string exePath, int pid, DateTime launchedUtc)
    {
        try
        {
            var directory = new DirectoryInfo(DirectoryFor(exePath));
            if (!directory.Exists)
            {
                return null;
            }

            var report = directory.EnumerateFiles($"*-{pid}.txt")
                .Where(file => file.LastWriteTimeUtc >= launchedUtc - ClockSlack)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            return report is null ? null : new CrashReport(report.FullName, ReadSummary(report.FullName), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CrashReport Bundle(CrashReport report)
    {
        var dump = Path.ChangeExtension(report.ReportPath, ".dmp");
        var bundle = Path.ChangeExtension(report.ReportPath, ".zip");
        if (!File.Exists(dump))
        {
            return File.Exists(bundle) ? report with { BundlePath = bundle } : report;
        }

        var partial = bundle + ".partial";
        try
        {
            using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(report.ReportPath, Path.GetFileName(report.ReportPath),
                    CompressionLevel.Fastest);
                archive.CreateEntryFromFile(dump, Path.GetFileName(dump), CompressionLevel.Fastest);
            }

            File.Move(partial, bundle, overwrite: true);
            File.Delete(dump);
            return report with { BundlePath = bundle };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(partial);
            return report;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string Describe(string title, string exePath, int exitCode, CrashReport? report) =>
        report is not null
            ? $"{title} crashed: {report.Summary}. Crash report: {report.SharePath}"
            : $"{title} stopped unexpectedly (exit code 0x{unchecked((uint)exitCode):X8}). "
              + $"Any crash report is saved in {DirectoryFor(exePath)}";

    private static string ReadSummary(string path)
    {
        const string fallback = "see the report for details";
        try
        {
            foreach (var line in File.ReadLines(path).Take(8))
            {
                if (line.StartsWith(SummaryPrefix, StringComparison.Ordinal))
                {
                    return line[SummaryPrefix.Length..].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }

        return fallback;
    }
}
