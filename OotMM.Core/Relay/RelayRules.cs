namespace OotMM.Core.Relay;

public enum DeathLinkMode
{
    Off,
    Team,
    Global,
}

public static class DeathLinkModes
{
    public static string Tag(DeathLinkMode mode) => mode switch
    {
        DeathLinkMode.Team => "team",
        DeathLinkMode.Global => "global",
        _ => "off",
    };

    public static DeathLinkMode? Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "off" or "false" or "no" or "0" => DeathLinkMode.Off,
        "team" => DeathLinkMode.Team,
        "global" or "true" or "yes" or "on" or "1" => DeathLinkMode.Global,
        _ => null,
    };
}

public sealed class RelayRules
{
    public const int DefaultPvpDamagePercent = 100;

    public bool Pvp { get; set; }
    public bool FriendlyFire { get; set; }
    public int PvpDamagePercent { get; set; } = DefaultPvpDamagePercent;
    public DeathLinkMode DeathLink { get; set; }
    public bool TimerEnabled { get; set; } = true;
    public int TimerMinPercent { get; set; }
    public int TimerMaxPercent { get; set; } = 250;
    public bool AllowManifestBootstrap { get; set; } = true;
    public TrapTuning Traps { get; set; } = new();
}

public static class PvpDamage
{
    public const int MinPercent = 10;
    public const int MaxPercent = 1000;

    public static int Clamp(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);
}

public sealed record VerifiedClientIdentity(
    string Room,
    string SettingsHash,
    string TeamId,
    ushort PlayerId,
    ushort PlayerCount);

public static class SideChannelPolicy
{
    public static bool IsRoomPacketAllowed(string line, VerifiedClientIdentity client)
    {
        if (client.Room.Length == 0 || client.PlayerId == 0 || client.PlayerId > client.PlayerCount
            || RelayProtocol.ExtractStringField(line, "seedId") != client.Room
            || RelayProtocol.ExtractStringField(line, "teamId") != client.TeamId)
        {
            return false;
        }

        var type = RelayProtocol.ExtractStringField(line, "type");
        var sourcePlayer = RelayProtocol.ExtractIntField(line, "sourcePlayer");
        switch (type)
        {
            case RelayProtocol.WorldFlag:
            {
                var kind = RelayProtocol.ExtractStringField(line, "kind");
                return (kind is "chest" or "switch" or "collect")
                       && GameInfo.TryFromSeedTag(RelayProtocol.ExtractStringField(line, "flagGame")) is not null
                       && RelayProtocol.ExtractIntField(line, "sceneId", -1) >= 0
                       && RelayProtocol.ExtractIntField(line, "flagIndex", -1) >= 0
                       && RelayProtocol.ExtractIntField(line, "playerId") == client.PlayerId
                       && RelayProtocol.ExtractIntField(line, "playerCount") == client.PlayerCount
                       && RelayProtocol.ExtractStringField(line, "settingsHash") == client.SettingsHash;
            }
            case PresencePackets.PlayerState:
                return GameInfo.TryFromSeedTag(RelayProtocol.ExtractStringField(line, "game")) is not null
                       && sourcePlayer == client.PlayerId;
            case PresencePackets.PvpHit:
            {
                var target = RelayProtocol.ExtractIntField(line, "targetPlayer");
                var damage = RelayProtocol.ExtractIntField(line, "damage");
                return sourcePlayer == client.PlayerId && target != 0 && target <= client.PlayerCount
                       && RelayProtocol.ExtractIntField(line, "seq") != 0
                       && damage is >= 1 and <= 255;
            }
            case PresencePackets.DeathLink:
                return sourcePlayer == client.PlayerId
                       && RelayProtocol.ExtractIntField(line, "seq") != 0;
            case PresencePackets.ModAnnounce:
                return sourcePlayer == client.PlayerId
                       && RelayProtocol.ExtractStringField(line, "hash").Length > 0
                       && RelayProtocol.ExtractU64Field(line, "size") != 0;
            case PresencePackets.ModRequest:
            {
                var target = RelayProtocol.ExtractIntField(line, "targetPlayer");
                return sourcePlayer == client.PlayerId && target != 0 && target <= client.PlayerCount
                       && RelayProtocol.ExtractStringField(line, "hash").Length > 0;
            }
            case PresencePackets.ModChunk:
            {
                var total = RelayProtocol.ExtractU64Field(line, "total");
                return sourcePlayer == client.PlayerId
                       && RelayProtocol.ExtractStringField(line, "hash").Length > 0
                       && total != 0 && RelayProtocol.ExtractU64Field(line, "seq") < total
                       && RelayProtocol.ExtractStringField(line, "data").Length > 0;
            }
            default:
                return false;
        }
    }
}

public static class PresencePackets
{
    public const string PlayerState = "OOTMM_PLAYER_STATE";
    public const string PvpHit = "OOTMM_PVP_HIT";
    public const string DeathLink = "OOTMM_DEATH_LINK";
    public const string ModAnnounce = "OOTMM_MOD_ANNOUNCE";
    public const string ModRequest = "OOTMM_MOD_REQUEST";
    public const string ModChunk = "OOTMM_MOD_CHUNK";
}
