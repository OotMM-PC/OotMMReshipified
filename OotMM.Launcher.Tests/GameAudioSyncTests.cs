using System.Text.Json.Nodes;
using OotMM.Core;
using OotMM.Launcher.Models;

namespace OotMM.Launcher.Tests;

public sealed class GameAudioSyncTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ootmm-audio-sync-tests", Guid.NewGuid().ToString("N"));
    private readonly string _ootExe;
    private readonly string _mmExe;

    public GameAudioSyncTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "SoH"));
        Directory.CreateDirectory(Path.Combine(_root, "2ship"));
        _ootExe = Path.Combine(_root, "SoH", "soh.exe");
        _mmExe = Path.Combine(_root, "2ship", "2ship.exe");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private string OotConfig => GameAudioSync.ConfigPath(_ootExe, Game.Oot);
    private string MmConfig => GameAudioSync.ConfigPath(_mmExe, Game.Mm);

    private static void Write(string path, string json, DateTime stamp)
    {
        File.WriteAllText(path, json);
        File.SetLastWriteTimeUtc(path, stamp);
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    [Fact]
    public void MmLaunchTakesVolumesFromNewerOotConfig()
    {
        Write(OotConfig, """{"CVars":{"gSettings":{"Volume":{"Master":4,"SFX":50}}},"Window":{"AudioBackend":"wasapi"}}""",
            DateTime.UtcNow);
        Write(MmConfig, """{"CVars":{"gSettings":{"Audio":{"MasterVolume":0.31,"AmbienceVolume":0.2}}},"Window":{"Width":640}}""",
            DateTime.UtcNow.AddMinutes(-5));

        GameAudioSync.PrepareLaunch(_ootExe, _mmExe, Game.Mm);

        var audio = Read(MmConfig)["CVars"]!["gSettings"]!["Audio"]!;
        Assert.Equal(0.04, audio["MasterVolume"]!.GetValue<double>(), 3);
        Assert.Equal(0.5, audio["SoundEffectsVolume"]!.GetValue<double>(), 3);
        Assert.Equal(1.0, audio["MainMusicVolume"]!.GetValue<double>(), 3);
        Assert.Equal(0.2, audio["AmbienceVolume"]!.GetValue<double>(), 3);
        Assert.Equal(640, Read(MmConfig)["Window"]!["Width"]!.GetValue<int>());
    }

    [Fact]
    public void OotLaunchTakesVolumesFromNewerMmConfigAndAppliesOotDefaults()
    {
        Write(MmConfig, """{"CVars":{"gSettings":{"Audio":{"MasterVolume":0.31}}}}""", DateTime.UtcNow);
        Write(OotConfig, """{"CVars":{"gSettings":{"Volume":{"Master":4}}}}""", DateTime.UtcNow.AddMinutes(-5));

        GameAudioSync.PrepareLaunch(_ootExe, _mmExe, Game.Oot);

        var volume = Read(OotConfig)["CVars"]!["gSettings"]!["Volume"]!;
        Assert.Equal(31, volume["Master"]!.GetValue<int>());
        Assert.Equal(100, volume["Fanfare"]!.GetValue<int>());
    }

    [Fact]
    public void OlderSourceLeavesTargetVolumesAlone()
    {
        Write(OotConfig, """{"CVars":{"gSettings":{"Volume":{"Master":4}}}}""", DateTime.UtcNow.AddMinutes(-5));
        var original = """{"CVars":{"gSettings":{"Audio":{"MasterVolume":0.31}}}}""";
        Write(MmConfig, original, DateTime.UtcNow);

        GameAudioSync.PrepareLaunch(_ootExe, _mmExe, Game.Mm);

        Assert.Equal(original, File.ReadAllText(MmConfig));
    }

    [Fact]
    public void NullAudioBackendIsRemoved()
    {
        Write(MmConfig, """{"Window":{"AudioBackend":"null","Width":640}}""", DateTime.UtcNow);

        GameAudioSync.PrepareLaunch(_ootExe, _mmExe, Game.Mm);

        var window = Read(MmConfig)["Window"]!.AsObject();
        Assert.False(window.ContainsKey("AudioBackend"));
        Assert.Equal(640, window["Width"]!.GetValue<int>());
    }

    [Fact]
    public void UnreadableTargetIsNotOverwritten()
    {
        Write(OotConfig, """{"CVars":{"gSettings":{"Volume":{"Master":4}}}}""", DateTime.UtcNow);
        Write(MmConfig, "{ broken", DateTime.UtcNow.AddMinutes(-5));

        GameAudioSync.PrepareLaunch(_ootExe, _mmExe, Game.Mm);

        Assert.Equal("{ broken", File.ReadAllText(MmConfig));
    }
}
