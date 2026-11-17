using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using OotMM.Core.Models;

namespace OotMM.Launcher.Models;

public sealed class UiSettings
{
    public string SeedPath { get; set; } = "";
    public string OotExePath { get; set; } = "";
    public string MmExePath { get; set; } = "";

    public string MultiworldAddress { get; set; } = "127.0.0.1:42801";
    public string PlayerName { get; set; } = "";
    public bool HostRelay { get; set; }
    public int RelayPort { get; set; } = 42801;
    public bool EnablePvp { get; set; }
    public bool EnableFriendlyFire { get; set; }
    public int PvpDamagePercent { get; set; } = OotMM.Core.Relay.RelayRules.DefaultPvpDamagePercent;
    public OotMM.Core.Relay.DeathLinkMode DeathLink { get; set; }
    public bool HostContentFilters { get; set; } = true;
    public bool HostEntranceMap { get; set; } = true;

    public bool EnableDeathLink
    {
        set
        {
            if (value)
            {
                DeathLink = OotMM.Core.Relay.DeathLinkMode.Global;
            }
        }
    }

    public List<string> PlayerModelOot { get; set; } = [];
    public List<string> PlayerModelMm { get; set; } = [];
    public bool PortOotAdultModel { get; set; } = true;
    public bool PortOotChildModel { get; set; }
    public int MaxModelSyncMb { get; set; } = ModelSyncPolicy.DefaultMaxMegabytes;

    public int Theme { get; set; }

    public bool DevMode { get; set; }
    public bool SpeedMatchFramerate { get; set; }
    public bool HoldToZTarget { get; set; }
    public bool RemoteItemPrompts { get; set; } = true;
    public bool FairySpotPads { get; set; }
    public bool PotRolling { get; set; } = true;
    public bool ContentFilters { get; set; }
    public bool EntranceMap { get; set; } = true;
    public OotMM.Core.TrapTuning Traps { get; set; } = new();

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
        }

        return new UiSettings();
    }

    public void Save()
    {
        OotMM.Core.DurableFile.WriteAtomic(
            FilePath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}
