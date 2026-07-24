using System.Text.Json;

namespace OotMM.Core;

public sealed record GameIpcHello(
    int ProtocolMajor,
    int ProtocolMinor,
    string Token,
    Game Game,
    int Pid,
    string Build);

public sealed record InventoryDebugSet(string ItemId, uint Value);

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
        public const string InventorySnapshot = "inventorySnapshot";
        public const string InventoryDelta = "inventoryDelta";
        public const string InventoryDebugSet = "inventoryDebugSet";
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

    public static string BuildTransitionAccepted(CrossGameTransition transition) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.TransitionAccepted,
            toGame = GameInfo.ToSeedTag(transition.ToGame),
            toEntrance = transition.ToEntrance,
            toNativeId = transition.ToNativeId,
        });

    public static string BuildInventorySnapshot(
        ulong revision, IReadOnlyDictionary<string, uint> items) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.InventorySnapshot,
            revision,
            items,
        });

    public static string BuildInventoryDelta(ulong revision, string itemId, uint value) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.InventoryDelta,
            revision,
            items = new Dictionary<string, uint> { [itemId] = value },
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

    public static bool TryParseCrossGameTransition(
        string json, out CrossGameTransition? transition, out string error)
    {
        transition = null;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != MessageType.CrossGameTransition)
            {
                error = "message is not a cross-game transition";
                return false;
            }

            var fromGame = GameInfo.TryFromSeedTag(ReadString(root, "fromGame"));
            var toGame = GameInfo.TryFromSeedTag(ReadString(root, "toGame"));
            var toEntrance = ReadString(root, "toEntrance");
            if (fromGame is null || toGame is null || fromGame == toGame || toEntrance.Length == 0)
            {
                error = "cross-game transition has invalid games or destination";
                return false;
            }

            transition = new CrossGameTransition(
                fromGame.Value,
                toGame.Value,
                toEntrance,
                ReadU32(root, "toNativeId"),
                ReadU32(root, "ootAge"));
            return true;
        }
        catch (JsonException)
        {
            error = "cross-game transition is not valid JSON";
            return false;
        }
    }

    public static bool TryParseInventoryDebugSet(
        string json, out InventoryDebugSet? update, out string error)
    {
        update = null;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != MessageType.InventoryDebugSet)
            {
                error = "message is not an inventory debug update";
                return false;
            }

            var itemId = ReadString(root, "itemId");
            if (!SessionInventory.IsValidItemId(itemId)
                || !root.TryGetProperty("value", out var value)
                || !value.TryGetUInt32(out var parsedValue)
                || parsedValue > SessionInventory.MaxValue)
            {
                error = "inventory debug update is invalid";
                return false;
            }

            update = new InventoryDebugSet(itemId, parsedValue);
            return true;
        }
        catch (JsonException)
        {
            error = "inventory debug update is not valid JSON";
            return false;
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

    private static uint? ReadU32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetUInt32(out var parsed)
            ? parsed
            : null;
}
