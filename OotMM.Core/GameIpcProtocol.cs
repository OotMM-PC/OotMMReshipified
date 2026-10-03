using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OotMM.Core;

public sealed record GameIpcHello(
    int ProtocolMajor,
    int ProtocolMinor,
    string Token,
    Game Game,
    int Pid,
    string Build);

public sealed record InventoryDebugSet(string ItemId, uint Value);
public sealed record DebugPresentItem(string ItemId, ushort RecipientPlayer);

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
        public const string CompletedChecks = "completedChecks";
        public const string DeliverItem = "deliverItem";
        public const string ItemApplied = "itemApplied";
        public const string InventorySnapshot = "inventorySnapshot";
        public const string InventoryDelta = "inventoryDelta";
        public const string InventoryDebugSet = "inventoryDebugSet";
        public const string DebugPresentItem = "debugPresentItem";
        public const string PresentItem = "presentItem";
        public const string LocalPlayerPose = "localPlayerPose";
        public const string RemotePlayerPoses = "remotePlayerPoses";
        public const string PvpHit = "pvpHit";
        public const string DeathLink = "deathLink";
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
            checkRewardProtocol = 1,
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

    public static string BuildPresentItem(
        ItemRef item,
        Game destinationGame,
        ushort sourcePlayer,
        ushort recipientPlayer,
        bool debug,
        ushort viewerPlayer = 0,
        string? checkKey = null) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.PresentItem,
            itemId = item.Id,
            itemName = item.Name.Length > 0 ? item.Name : item.Id,
            itemGame = GameInfo.ToSeedTag(item.Game),
            destinationGame = GameInfo.ToSeedTag(destinationGame),
            sourcePlayer,
            recipientPlayer,
            viewerPlayer = viewerPlayer == 0 ? recipientPlayer : viewerPlayer,
            debug,
            check = checkKey,
        });

    /// <summary>The live roster pushed to the running game. The type must stay the first key:
    /// the game matches this frame by prefix so it can defer parsing off its render path.</summary>
    public static string BuildRemotePlayerPoses(IReadOnlyList<string> poses)
    {
        var builder = new StringBuilder("{\"type\":\"");
        builder.Append(MessageType.RemotePlayerPoses).Append("\",\"poses\":[");
        for (var i = 0; i < poses.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(poses[i]);
        }

        return builder.Append("]}").ToString();
    }

    /// <summary>A single PvP hit. Kept out of the roster because that frame is last-value:
    /// superseding it must never drop an event the victim has to apply.</summary>
    public static string BuildPvpHit(string hitBody) =>
        "{\"type\":\"" + MessageType.PvpHit + "\",\"hit\":" + hitBody + "}";

    public static string BuildDeathLink(string deathBody) =>
        "{\"type\":\"" + MessageType.DeathLink + "\",\"death\":" + deathBody + "}";

    public static string BuildCheckAccepted(string checkKey) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.CheckAccepted,
            check = checkKey,
        });

    /// <summary>The full completed-check snapshot sent when a game connects, so a fresh
    /// process starts from the launcher's durable record instead of an empty set.</summary>
    public static string BuildCompletedChecks(IEnumerable<string> checkKeys) =>
        JsonSerializer.Serialize(new
        {
            type = MessageType.CompletedChecks,
            checks = checkKeys,
        });

    public static bool TryParseCheckCollected(string json, out string checkKey, out string error) =>
        TryParseCheckCollected(json, out checkKey, out _, out error);

    public static bool TryParseCheckCollected(
        string json, out string checkKey, out bool presentationRequested, out string error)
    {
        presentationRequested = false;
        checkKey = "";
        error = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != MessageType.CheckCollected)
            {
                error = "message is not a check collection report";
                return false;
            }

            var check = ReadString(root, "check");
            if (check.Length == 0 || GameInfo.TryFromSeedTag(SplitCheckKey(check, out var checkId)) is null
                || checkId.Length == 0)
            {
                error = "check collection report has an invalid check key";
                return false;
            }

            if (root.TryGetProperty("present", out var present))
            {
                if (present.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "check presentation flag must be boolean";
                    return false;
                }
                presentationRequested = present.GetBoolean();
            }
            checkKey = check;
            return true;
        }
        catch (JsonException)
        {
            error = "check collection report is not valid JSON";
            return false;
        }
    }

    /// <summary>Splits an "oot:CHECK_ID" key into its game tag (returned) and check id.</summary>
    public static string SplitCheckKey(string checkKey, out string checkId)
    {
        var separator = checkKey.IndexOf(':');
        if (separator <= 0 || separator == checkKey.Length - 1)
        {
            checkId = "";
            return "";
        }

        checkId = checkKey[(separator + 1)..];
        return checkKey[..separator];
    }

    /// <summary>Extracts the bare pose object from a game's localPlayerPose envelope.</summary>
    public static bool TryParseLocalPlayerPose(string json, out string pose) =>
        TryParseEnvelopeBody(json, MessageType.LocalPlayerPose, "pose", out pose);

    public static bool TryParsePvpHit(string json, out string hit) =>
        TryParseEnvelopeBody(json, MessageType.PvpHit, "hit", out hit);

    public static bool TryParseDeathLink(string json, out string death) =>
        TryParseEnvelopeBody(json, MessageType.DeathLink, "death", out death);

    private static bool TryParseEnvelopeBody(
        string json, string expectedType, string bodyName, out string body)
    {
        body = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != expectedType
                || !root.TryGetProperty(bodyName, out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            body = element.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Stamps a bare presence body with what only the launcher knows: packet type,
    /// display name, and the room identity the relay's allowlist demands on every line.</summary>
    public static string BuildRelayPresenceLine(
        string relayType, string body, Seed seed, string clientName)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if (node is not JsonObject json)
            {
                return "";
            }

            json["type"] = relayType;
            json["seedId"] = seed.SeedId;
            json["teamId"] = seed.TeamId;
            // The relay authorises against the launcher's identity, so the launcher asserts the
            // sender rather than trusting whatever the game reported.
            json["sourcePlayer"] = seed.PlayerId;
            if (clientName.Length > 0)
            {
                json["clientName"] = clientName;
            }

            return json.ToJsonString();
        }
        catch (JsonException)
        {
            return "";
        }
    }

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

    public static bool TryParseDebugPresentItem(
        string json, out DebugPresentItem? request, out string error)
    {
        request = null;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "type") != MessageType.DebugPresentItem)
            {
                error = "message is not a debug item presentation request";
                return false;
            }

            var itemId = ReadString(root, "itemId");
            var recipientPlayer = ReadInt(root, "recipientPlayer");
            if (!SessionInventory.IsValidItemId(itemId)
                || recipientPlayer is < 1 or > ushort.MaxValue)
            {
                error = "debug item presentation request is invalid";
                return false;
            }

            request = new DebugPresentItem(itemId, (ushort)recipientPlayer);
            return true;
        }
        catch (JsonException)
        {
            error = "debug item presentation request is not valid JSON";
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
