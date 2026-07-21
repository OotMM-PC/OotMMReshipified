namespace OotMM.Core;

/// <summary>The roster-meta line the games poll every frame from presence.in;
/// carries session rules and the live game-speed setting.</summary>
public sealed record RosterMeta(
    bool ShowNames,
    bool Pvp,
    bool DeathLink,
    int SpeedPercent,
    bool SpeedSmooth,
    string TransportState,
    uint PendingChecks);

public static class PresenceRoster
{
    /// <summary>Byte-compatible with WriteRosterFile's meta line in Presence.cpp.</summary>
    public static string BuildMetaLine(RosterMeta meta) =>
        $"{{\"type\":\"OOTMM_ROSTER_META\",\"showNames\":{(meta.ShowNames ? "true" : "false")}"
        + $",\"pvp\":{(meta.Pvp ? "true" : "false")}"
        + $",\"deathLink\":{(meta.DeathLink ? "true" : "false")}"
        + $",\"speedPercent\":{meta.SpeedPercent}"
        + $",\"speedSmooth\":{(meta.SpeedSmooth ? "true" : "false")}"
        + $",\"transportState\":\"{meta.TransportState}\""
        + $",\"pendingChecks\":{meta.PendingChecks}}}\n";

    public static bool Write(string path, RosterMeta meta) =>
        DurableFile.WriteAtomic(path, BuildMetaLine(meta));
}
