namespace OotMM.Core;

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
