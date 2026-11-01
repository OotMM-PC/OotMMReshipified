using System.IO.Compression;
using OotMM.Launcher.Models;

namespace OotMM.Launcher.Tests;

public sealed class GameSetupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ootmm-game-setup-tests", Guid.NewGuid().ToString("N"));

    public GameSetupTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void BundledGamesAreFoundBesideTheLauncherFolder()
    {
        var launcher = Directory.CreateDirectory(Path.Combine(_directory, "Launcher")).FullName;
        Directory.CreateDirectory(Path.Combine(_directory, "SoH"));
        Directory.CreateDirectory(Path.Combine(_directory, "2ship", "Release"));
        File.WriteAllText(Path.Combine(_directory, "SoH", "soh.exe"), "");
        File.WriteAllText(Path.Combine(_directory, "2ship", "Release", "2ship.exe"), "");

        Assert.Equal(Path.Combine(_directory, "SoH", "soh.exe"), GameSetup.LocateBundled(true, launcher));
        Assert.Equal(Path.Combine(_directory, "2ship", "Release", "2ship.exe"), GameSetup.LocateBundled(false, launcher));
    }

    [Fact]
    public void MissingBundledGameIsNull() =>
        Assert.Null(GameSetup.LocateBundled(true, Directory.CreateDirectory(Path.Combine(_directory, "Alone")).FullName));

    [Fact]
    public void LegacyBaseArchiveRequiresReextractionForSoulOverlay()
    {
        var executable = CreateOotInstall(includeSoulOverlay: false);

        Assert.Contains("oot.o2r", GameSetup.MissingArchives(executable, isOot: true, needMq: false));
    }

    [Fact]
    public void UpdatedBaseArchiveIsReady()
    {
        var executable = CreateOotInstall(includeSoulOverlay: true);

        Assert.Empty(GameSetup.MissingArchives(executable, isOot: true, needMq: false));
    }

    [Fact]
    public void StaleArchiveIsHiddenDuringExtraction()
    {
        var executable = CreateOotInstall(includeSoulOverlay: false);
        var archivePath = Path.Combine(_directory, "oot.o2r");

        var refresh = GameSetup.PrepareBaseArchiveRefresh(executable, isOot: true);

        Assert.NotNull(refresh);
        Assert.False(File.Exists(archivePath));
        Assert.True(File.Exists(refresh.BackupPath));
    }

    [Fact]
    public void FailedExtractionRestoresStaleArchive()
    {
        var executable = CreateOotInstall(includeSoulOverlay: false);
        var archivePath = Path.Combine(_directory, "oot.o2r");
        var refresh = GameSetup.PrepareBaseArchiveRefresh(executable, isOot: true);

        var updated = GameSetup.FinishBaseArchiveRefresh(refresh!);

        Assert.False(updated);
        Assert.True(File.Exists(archivePath));
        Assert.False(File.Exists(refresh!.BackupPath));
    }

    [Fact]
    public void SuccessfulExtractionRemovesBackup()
    {
        var executable = CreateOotInstall(includeSoulOverlay: false);
        var refresh = GameSetup.PrepareBaseArchiveRefresh(executable, isOot: true);
        CreateBaseArchive(includeSoulOverlay: true);

        var updated = GameSetup.FinishBaseArchiveRefresh(refresh!);

        Assert.True(updated);
        Assert.False(File.Exists(refresh!.BackupPath));
        Assert.Empty(GameSetup.MissingArchives(executable, isOot: true, needMq: false));
    }

    private string CreateOotInstall(bool includeSoulOverlay)
    {
        var executable = Path.Combine(_directory, "soh.exe");
        File.WriteAllBytes(executable, []);
        File.WriteAllBytes(Path.Combine(_directory, "soh.o2r"), []);
        CreateBaseArchive(includeSoulOverlay);
        return executable;
    }

    private void CreateBaseArchive(bool includeSoulOverlay)
    {
        using var archive = ZipFile.Open(Path.Combine(_directory, "oot.o2r"), ZipArchiveMode.Create);
        if (includeSoulOverlay)
        {
            archive.CreateEntry("objects/object_gi_sutaru/gGiSkulltulaTokenSkullDL");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
