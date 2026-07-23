using System.Text.Json;

namespace OotMM.Core;

public sealed record GameIpcHello(
    int ProtocolMajor,
    int ProtocolMinor,
    string Token,
    Game Game,
    int Pid,
    string Build);

public static class GameIpcProtocol
{
    public const int ProtocolMajor = 1;
    public const int ProtocolMinor = 0;

    public static class MessageType
    {
        public const string Hello = "hello";
        public const string Welcome = "welcome";
        public const string Heartbeat = "heartbeat";
        public const string ProtocolError = "protocolError";
        public const string SettingsSnapshot = "settingsSnapshot";
        public const string SettingsDelta = "settingsDelta";
        public const string SettingsApplied = "settingsApplied";
        public const string CheckCollected = "checkCollected";
        public const string CheckAccepted = "checkAccepted";
        public const string DeliverItem = "deliverItem";
        public const string ItemApplied = "itemApplied";
        public const string LocalPlayerPose = "localPlayerPose";
        public const string RemotePlayerPoses = "remotePlayerPoses";
        public const string ModelReady = "modelReady";
        public const string ModelMounted = "modelMounted";
        public const string CrossGameTransition = "crossGameTransition";
        public const string TransitionAccepted = "transitionAccepted";
    }

    public static string BuildWelcome(
        string sessionId, string saveId, uint playerId, Game game) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.Welcome,
            protocolMajor = ProtocolMajor,
            protocolMinor = ProtocolMinor,
            sessionId,
            saveId,
            playerId,
            game = GameInfo.ToSeedTag(game),
        });

    public static string BuildSettingsSnapshot(long revision, RosterMeta settings) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.SettingsSnapshot,
            revision,
            settings = new
            {
                showNames = settings.ShowNames,
                pvp = settings.Pvp,
                deathLink = settings.DeathLink,
                speedPercent = settings.SpeedPercent,
                speedSmooth = settings.SpeedSmooth,
                transportState = settings.TransportState,
                pendingChecks = settings.PendingChecks,
            },
        });

    public static string BuildProtocolError(string message) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.ProtocolError,
            protocolMajor = ProtocolMajor,
            protocolMinor = ProtocolMinor,
            message,
        });

    public static bool TryParseHello(string json, out GameIpcHello? hello, out string error)
    {
        hello = null;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != MessageType.Hello)
            {
                error = "first message must be hello";
                return false;
            }

            var game = GameInfo.TryFromSeedTag(ReadString(root, "game"));
            if (game is null)
            {
                error = "hello contains an invalid game";
                return false;
            }

            hello = new GameIpcHello(
                ReadInt(root, "protocolMajor"),
                ReadInt(root, "protocolMinor"),
                ReadString(root, "token"),
                game.Value,
                ReadInt(root, "pid"),
                ReadString(root, "build"));
            return true;
        }
        catch (JsonException)
        {
            error = "hello is not valid JSON";
            return false;
        }
    }

    public static string ReadType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadString(document.RootElement, "type")
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
}
