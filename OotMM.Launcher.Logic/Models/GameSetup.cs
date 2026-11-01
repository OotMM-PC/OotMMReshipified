using System.IO;
using System.IO.Compression;

namespace OotMM.Launcher.Models;

public sealed record GameArchiveRefresh(string ArchivePath, string BackupPath);

public static class GameSetup
{
    private const string SoulOverlay = "objects/object_gi_sutaru/gGiSkulltulaTokenSkullDL";

    public static IReadOnlyList<string> MissingArchives(string exePath, bool isOot, bool needMq)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return [];
        }

        var dir = Path.GetDirectoryName(exePath)!;
        var required = isOot
            ? new List<string> { "soh.o2r", "oot.o2r" }
            : new List<string> { "2ship.o2r", "mm.o2r" };
        if (isOot && needMq)
        {
            required.Add("oot-mq.o2r");
        }

        return required.Where(name =>
        {
            var path = Path.Combine(dir, name);
            return !File.Exists(path) ||
                   (name is "oot.o2r" or "mm.o2r" && !ArchiveContains(path, SoulOverlay));
        }).ToList();
    }

    private static bool ArchiveContains(string path, string entry)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.GetEntry(entry) is not null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static GameArchiveRefresh? PrepareBaseArchiveRefresh(string exePath, bool isOot)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        var archivePath = Path.Combine(dir, isOot ? "oot.o2r" : "mm.o2r");
        var backupPath = archivePath + ".ootmm-backup";
        if (!File.Exists(archivePath))
        {
            return File.Exists(backupPath)
                ? new GameArchiveRefresh(archivePath, backupPath)
                : null;
        }

        if (ArchiveContains(archivePath, SoulOverlay))
        {
            return null;
        }

        if (File.Exists(backupPath))
        {
            File.Delete(archivePath);
        }
        else
        {
            File.Move(archivePath, backupPath);
        }

        return new GameArchiveRefresh(archivePath, backupPath);
    }

    public static bool FinishBaseArchiveRefresh(GameArchiveRefresh refresh)
    {
        if (File.Exists(refresh.ArchivePath) && ArchiveContains(refresh.ArchivePath, SoulOverlay))
        {
            File.Delete(refresh.BackupPath);
            return true;
        }

        if (File.Exists(refresh.BackupPath))
        {
            File.Delete(refresh.ArchivePath);
            File.Move(refresh.BackupPath, refresh.ArchivePath);
        }

        return false;
    }
}
