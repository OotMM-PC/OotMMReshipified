using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using OotMM.Core.Models;

namespace OotMM.Launcher.Models;

/// <summary>
/// Launcher preferences persisted next to the executable (the launcher runs
/// portable, like the game ports). Covers what the UI itself needs; when the
/// LauncherConfig module is ported it can absorb or replace this file — the
/// name is launcher.ui.json precisely so the two never collide.
/// </summary>
public sealed class UiSettings
{
    public string SeedPath { get; set; } = "";
    public string OotExePath { get; set; } = "";
    public string MmExePath { get; set; } = "";

    public string MultiworldAddress { get; set; } = "127.0.0.1:42801";
    public string PlayerName { get; set; } = "";
    public bool HostRelay { get; set; }
    public int RelayPort { get; set; } = 42801; // default relay port, see docs/multiworld-v4-design.md
    public bool EnablePvp { get; set; }
    public List<string> PlayerModelOot { get; set; } = [];
    public List<string> PlayerModelMm { get; set; } = [];
    /// <summary>Port the selected OoT model into MM's adult form at launch.</summary>
    public bool PortOotAdultModel { get; set; } = true;
    /// <summary>Port the selected OoT child model into MM's human form at launch.</summary>
    public bool PortOotChildModel { get; set; }
    /// <summary>Model sets larger than this are neither sent nor accepted; that puppet keeps
    /// the vanilla fill instead.</summary>
    public int MaxModelSyncMb { get; set; } = ModelSyncPolicy.DefaultMaxMegabytes;

    /// <summary>0 = follow the active game, 1 = always OoT gold, 2 = always MM violet.</summary>
    public int Theme { get; set; }

    public bool DevMode { get; set; }
    public bool SpeedMatchFramerate { get; set; }

    public double UiScale { get; set; } = 1.0;
    public bool ShowSidebar { get; set; } = true;

    [JsonIgnore]
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "launcher.ui.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath))
                       ?? new UiSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Corrupt or unreadable settings must never block the launcher from
            // starting; fall back to defaults and overwrite on the next save.
        }

        return new UiSettings();
    }

    public void Save()
    {
        // Atomic: a launcher killed mid-write must never truncate the file (a torn
        // file loads as defaults and the next save wipes every preference).
        OotMM.Core.DurableFile.WriteAtomic(
            FilePath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}
