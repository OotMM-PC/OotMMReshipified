using System.Text.Json;
using System.Text.Json.Nodes;
using OotMM.Core;

namespace OotMM.Launcher.Models;

public static class GameAudioSync
{
    public const string OotConfigName = "shipofharkinian.json";
    public const string MmConfigName = "2ship2harkinian.json";

    private sealed record Channel(string OotKey, int OotDefault, string MmKey);

    private static readonly Channel[] Channels =
    [
        new("Master", 40, "MasterVolume"),
        new("MainMusic", 100, "MainMusicVolume"),
        new("SubMusic", 100, "SubMusicVolume"),
        new("SFX", 100, "SoundEffectsVolume"),
        new("Fanfare", 100, "FanfareVolume"),
    ];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, IndentSize = 4 };

    public static string ConfigPath(string executable, Game game) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)) ?? ".",
            game == Game.Oot ? OotConfigName : MmConfigName);

    public static void PrepareLaunch(string ootExecutable, string mmExecutable, Game target)
    {
        var targetExecutable = target == Game.Oot ? ootExecutable : mmExecutable;
        var sourceExecutable = target == Game.Oot ? mmExecutable : ootExecutable;
        if (string.IsNullOrWhiteSpace(targetExecutable)) return;
        var targetPath = ConfigPath(targetExecutable, target);
        var sourcePath = string.IsNullOrWhiteSpace(sourceExecutable) ? null
            : ConfigPath(sourceExecutable, target == Game.Oot ? Game.Mm : Game.Oot);

        var targetConfig = Load(targetPath);
        if (targetConfig is null && File.Exists(targetPath)) return;
        targetConfig ??= new JsonObject();
        var changed = RepairAudioBackend(targetConfig);

        if (sourcePath is not null && File.Exists(sourcePath) && (!File.Exists(targetPath)
            || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(targetPath))
            && Load(sourcePath) is { } sourceConfig)
        {
            var volumes = target == Game.Oot ? ReadMm(sourceConfig) : ReadOot(sourceConfig);
            changed |= target == Game.Oot ? WriteOot(targetConfig, volumes) : WriteMm(targetConfig, volumes);
        }

        if (changed) DurableFile.WriteAtomic(targetPath, targetConfig.ToJsonString(Indented));
    }

    public static double[] ReadOot(JsonObject config)
    {
        var volume = Settings(config, false)?["Volume"] as JsonObject;
        return Channels.Select(channel => Math.Clamp(
            ReadNumber(volume?[channel.OotKey]) ?? channel.OotDefault, 0, 100) / 100.0).ToArray();
    }

    public static double[] ReadMm(JsonObject config)
    {
        var audio = Settings(config, false)?["Audio"] as JsonObject;
        return Channels.Select(channel => Math.Clamp(ReadNumber(audio?[channel.MmKey]) ?? 1.0, 0, 1)).ToArray();
    }

    private static bool WriteOot(JsonObject config, double[] volumes)
    {
        var volume = Child(Settings(config, true)!, "Volume");
        var changed = false;
        for (var i = 0; i < Channels.Length; i++)
        {
            var value = (int)Math.Round(volumes[i] * 100);
            if (ReadNumber(volume[Channels[i].OotKey]) is { } existing && (int)Math.Round(existing) == value) continue;
            volume[Channels[i].OotKey] = value;
            changed = true;
        }
        return changed;
    }

    private static bool WriteMm(JsonObject config, double[] volumes)
    {
        var audio = Child(Settings(config, true)!, "Audio");
        var changed = false;
        for (var i = 0; i < Channels.Length; i++)
        {
            var key = Channels[i].MmKey;
            var existing = ReadNumber(audio[key]) ?? 1.0;
            if (Math.Round(existing * 100) == Math.Round(volumes[i] * 100) && audio[key] is not null) continue;
            audio[key] = volumes[i];
            changed = true;
        }
        return changed;
    }

    private static bool RepairAudioBackend(JsonObject config)
    {
        if (config["Window"] is not JsonObject window) return false;
        if (window["AudioBackend"] is not JsonValue value || !value.TryGetValue(out string? backend)
            || !string.Equals(backend, "null", StringComparison.OrdinalIgnoreCase)) return false;
        window.Remove("AudioBackend");
        return true;
    }

    private static JsonObject? Settings(JsonObject config, bool create)
    {
        if (config["CVars"] is not JsonObject cvars)
        {
            if (!create) return null;
            cvars = new JsonObject();
            config["CVars"] = cvars;
        }
        return create ? Child(cvars, "gSettings") : cvars["gSettings"] as JsonObject;
    }

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject child) return child;
        child = new JsonObject();
        parent[name] = child;
        return child;
    }

    private static double? ReadNumber(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out double number) && double.IsFinite(number) ? number : null;

    private static JsonObject? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
